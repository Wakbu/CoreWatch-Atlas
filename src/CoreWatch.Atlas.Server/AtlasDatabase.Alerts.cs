using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<IReadOnlyList<AlertRule>> ListAlertRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, metric_type, threshold, severity, enabled FROM alert_rules ORDER BY id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rules = new List<AlertRule>();
        while (await reader.ReadAsync(cancellationToken)) rules.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3), reader.GetString(4), reader.GetInt64(5) == 1));
        return rules;
    }

    public async Task<AlertRule> CreateAlertRuleAsync(AlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRule(request);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO alert_rules (name, metric_type, threshold, severity, enabled) VALUES ($name,$metric,$threshold,$severity,$enabled); SELECT last_insert_rowid();";
        AddRuleParameters(command, request); var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new(id, request.Name.Trim(), request.MetricType, request.Threshold, request.Severity, request.Enabled);
    }

    public async Task<bool> UpdateAlertRuleAsync(long id, AlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRule(request); await using var connection = await OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE alert_rules SET name=$name,metric_type=$metric,threshold=$threshold,severity=$severity,enabled=$enabled WHERE id=$id;"; AddRuleParameters(command, request); command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteAlertRuleAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM alert_rules WHERE id=$id;"; command.Parameters.AddWithValue("$id", id); return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<AlertRecord>> ListAlertsAsync(bool activeOnly, int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT a.id,a.agent_id,g.host_name,a.rule_id,r.name,r.metric_type,a.severity,a.current_value,a.state,a.opened_at_utc,a.resolved_at_utc,a.acknowledged_at_utc,a.acknowledged_by FROM alerts a JOIN agents g ON g.agent_id=a.agent_id JOIN alert_rules r ON r.id=a.rule_id WHERE ($active=0 OR a.state='active') ORDER BY a.opened_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$active", activeOnly ? 1 : 0); command.Parameters.AddWithValue("$limit", limit); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var alerts = new List<AlertRecord>();
        while (await reader.ReadAsync(cancellationToken)) alerts.Add(ReadAlert(reader)); return alerts;
    }

    public async Task<bool> AcknowledgeAlertAsync(long id, string username, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "UPDATE alerts SET acknowledged_at_utc=$now,acknowledged_by=$username WHERE id=$id AND state='active' AND acknowledged_at_utc IS NULL;"; command.Parameters.AddWithValue("$now", FormatTimestamp(_timeProvider.GetUtcNow())); command.Parameters.AddWithValue("$username", username); command.Parameters.AddWithValue("$id", id); return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task EvaluateAlertsAsync(Guid agentId, JsonElement metrics, CancellationToken cancellationToken = default)
    {
        var rules = await ListAlertRulesAsync(cancellationToken); foreach (var rule in rules.Where(x => x.Enabled && x.MetricType != AlertMetricTypes.Offline)) await SetAlertStateAsync(agentId, rule, TryMetric(metrics, rule.MetricType), cancellationToken);
    }

    public async Task EvaluateOfflineAlertsAsync(TimeSpan offlineAfter, CancellationToken cancellationToken = default)
    {
        var rules = (await ListAlertRulesAsync(cancellationToken)).Where(x => x.Enabled && x.MetricType == AlertMetricTypes.Offline).ToArray(); if (rules.Length == 0) return;
        await using var connection = await OpenConnectionAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT agent_id,last_seen_at_utc FROM agents WHERE archived_at_utc IS NULL;"; await using var reader = await command.ExecuteReaderAsync(cancellationToken); var agents = new List<(Guid, DateTimeOffset?)>(); while(await reader.ReadAsync(cancellationToken)) agents.Add((Guid.Parse(reader.GetString(0)), reader.IsDBNull(1)?null:ParseTimestamp(reader.GetString(1))));
        var now = _timeProvider.GetUtcNow(); foreach(var agent in agents) foreach(var rule in rules) { var seconds = agent.Item2 is null ? double.PositiveInfinity : (now-agent.Item2.Value).TotalSeconds; await SetAlertStateAsync(agent.Item1, rule, seconds, cancellationToken); }
    }

    private async Task SetAlertStateAsync(Guid agentId, AlertRule rule, double value, CancellationToken cancellationToken)
    {
        var violated = value >= rule.Threshold; var now = _timeProvider.GetUtcNow(); await using var connection = await OpenConnectionAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var existing = connection.CreateCommand(); existing.Transaction=transaction; existing.CommandText="SELECT id FROM alerts WHERE agent_id=$agentId AND rule_id=$ruleId AND state='active';"; existing.Parameters.AddWithValue("$agentId",agentId.ToString("D")); existing.Parameters.AddWithValue("$ruleId",rule.Id); var activeId=await existing.ExecuteScalarAsync(cancellationToken);
        if(violated && activeId is null) { await using var insert=connection.CreateCommand(); insert.Transaction=transaction; insert.CommandText="INSERT INTO alerts(agent_id,rule_id,severity,current_value,state,opened_at_utc) VALUES($agentId,$ruleId,$severity,$value,'active',$now); SELECT last_insert_rowid();"; insert.Parameters.AddWithValue("$agentId",agentId.ToString("D")); insert.Parameters.AddWithValue("$ruleId",rule.Id); insert.Parameters.AddWithValue("$severity",rule.Severity); insert.Parameters.AddWithValue("$value",value); insert.Parameters.AddWithValue("$now",FormatTimestamp(now)); var id=Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken),CultureInfo.InvariantCulture); await QueueNotificationsAsync(connection,transaction,id,"opened",cancellationToken); }
        else if(violated) { await using var update=connection.CreateCommand(); update.Transaction=transaction; update.CommandText="UPDATE alerts SET current_value=$value,severity=$severity WHERE id=$id;"; update.Parameters.AddWithValue("$value",value); update.Parameters.AddWithValue("$severity",rule.Severity); update.Parameters.AddWithValue("$id",activeId); await update.ExecuteNonQueryAsync(cancellationToken); }
        else if(activeId is not null) { await using var resolve=connection.CreateCommand(); resolve.Transaction=transaction; resolve.CommandText="UPDATE alerts SET state='resolved',resolved_at_utc=$now,current_value=$value WHERE id=$id;"; resolve.Parameters.AddWithValue("$now",FormatTimestamp(now)); resolve.Parameters.AddWithValue("$value",value); resolve.Parameters.AddWithValue("$id",activeId); await resolve.ExecuteNonQueryAsync(cancellationToken); await QueueNotificationsAsync(connection,transaction,Convert.ToInt64(activeId,CultureInfo.InvariantCulture),"resolved",cancellationToken); }
        await transaction.CommitAsync(cancellationToken);
    }

    private static double TryMetric(JsonElement m,string type) { try { return type switch { "cpu"=>m.GetProperty("cpu").GetProperty("usageRatio").GetDouble()*100, "memory"=>100*(1-m.GetProperty("memory").GetProperty("availableBytes").GetDouble()/m.GetProperty("memory").GetProperty("totalBytes").GetDouble()), "disk"=>Disk(m), _=>0 }; } catch { return 0; } }
    private static double Disk(JsonElement m) { var total=0d;var free=0d;foreach(var x in m.GetProperty("fileSystems").EnumerateArray()){total+=x.GetProperty("totalBytes").GetDouble();free+=x.GetProperty("availableBytes").GetDouble();}return total==0?0:100*(1-free/total); }
    private static AlertRecord ReadAlert(SqliteDataReader r)=>new(r.GetInt64(0),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetInt64(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetDouble(7),r.GetString(8),ParseTimestamp(r.GetString(9)),r.IsDBNull(10)?null:ParseTimestamp(r.GetString(10)),r.IsDBNull(11)?null:ParseTimestamp(r.GetString(11)),r.IsDBNull(12)?null:r.GetString(12));
    private static void ValidateRule(AlertRuleRequest x){if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>80||!new[]{"cpu","memory","disk","offline"}.Contains(x.MetricType)||!new[]{"warning","critical"}.Contains(x.Severity)||x.Threshold<=0||(x.MetricType!="offline"&&x.Threshold>100)||x.Threshold>86400)throw new ArgumentException("Invalid alert rule.");}
    private static void AddRuleParameters(SqliteCommand c,AlertRuleRequest x){c.Parameters.AddWithValue("$name",x.Name.Trim());c.Parameters.AddWithValue("$metric",x.MetricType);c.Parameters.AddWithValue("$threshold",x.Threshold);c.Parameters.AddWithValue("$severity",x.Severity);c.Parameters.AddWithValue("$enabled",x.Enabled?1:0);}
    private static async Task QueueNotificationsAsync(SqliteConnection c,SqliteTransaction t,long id,string type,CancellationToken ct){await using var q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO notification_deliveries(alert_id,channel_id,event_type,created_at_utc) SELECT $id,id,$type,strftime('%Y-%m-%dT%H:%M:%fZ','now') FROM notification_channels WHERE enabled=1;";q.Parameters.AddWithValue("$id",id);q.Parameters.AddWithValue("$type",type);await q.ExecuteNonQueryAsync(ct);}
}
public sealed partial class AtlasDatabase
{
    public async Task<IReadOnlyList<NotificationChannel>> ListNotificationChannelsAsync(CancellationToken cancellationToken=default){await using var c=await OpenConnectionAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT id,name,url,enabled FROM notification_channels ORDER BY id;";await using var r=await q.ExecuteReaderAsync(cancellationToken);var result=new List<NotificationChannel>();while(await r.ReadAsync(cancellationToken))result.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3)==1));return result;}
    public async Task<NotificationChannel> CreateNotificationChannelAsync(NotificationChannelRequest x,CancellationToken ct=default){ValidateChannel(x);await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT INTO notification_channels(name,url,enabled) VALUES($name,$url,$enabled);SELECT last_insert_rowid();";AddChannel(q,x);var id=Convert.ToInt64(await q.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);return new(id,x.Name.Trim(),x.Url.Trim(),x.Enabled);}
    public async Task<bool> UpdateNotificationChannelAsync(long id,NotificationChannelRequest x,CancellationToken ct=default){ValidateChannel(x);await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE notification_channels SET name=$name,url=$url,enabled=$enabled WHERE id=$id;";AddChannel(q,x);q.Parameters.AddWithValue("$id",id);return await q.ExecuteNonQueryAsync(ct)==1;}
    public async Task<bool> DeleteNotificationChannelAsync(long id,CancellationToken ct=default){await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="DELETE FROM notification_channels WHERE id=$id;";q.Parameters.AddWithValue("$id",id);return await q.ExecuteNonQueryAsync(ct)==1;}
    public async Task<IReadOnlyList<(long Id,string Url,string Payload)>> GetPendingNotificationDeliveriesAsync(int limit,CancellationToken ct=default){await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT d.id,ch.url,json_object('event',d.event_type,'alertId',a.id,'agent',g.host_name,'rule',r.name,'severity',a.severity,'value',a.current_value,'state',a.state,'openedAtUtc',a.opened_at_utc) FROM notification_deliveries d JOIN notification_channels ch ON ch.id=d.channel_id JOIN alerts a ON a.id=d.alert_id JOIN agents g ON g.agent_id=a.agent_id JOIN alert_rules r ON r.id=a.rule_id WHERE d.delivered_at_utc IS NULL AND d.attempt_count<5 ORDER BY d.id LIMIT $limit;";q.Parameters.AddWithValue("$limit",limit);await using var r=await q.ExecuteReaderAsync(ct);var result=new List<(long,string,string)>();while(await r.ReadAsync(ct))result.Add((r.GetInt64(0),r.GetString(1),r.GetString(2)));return result;}
    public async Task MarkNotificationDeliveryAsync(long id,bool succeeded,string? error,CancellationToken ct=default){await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="UPDATE notification_deliveries SET attempt_count=attempt_count+1,delivered_at_utc=CASE WHEN $ok=1 THEN $now ELSE NULL END,last_error=$error WHERE id=$id;";q.Parameters.AddWithValue("$id",id);q.Parameters.AddWithValue("$ok",succeeded?1:0);q.Parameters.AddWithValue("$now",FormatTimestamp(_timeProvider.GetUtcNow()));q.Parameters.AddWithValue("$error",error??(object)DBNull.Value);await q.ExecuteNonQueryAsync(ct);}
    private static void ValidateChannel(NotificationChannelRequest x){if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>80||!Uri.TryCreate(x.Url,UriKind.Absolute,out var uri)||(uri.Scheme!="https"&&uri.Scheme!="http"))throw new ArgumentException("Invalid notification channel.");}
    private static void AddChannel(SqliteCommand q,NotificationChannelRequest x){q.Parameters.AddWithValue("$name",x.Name.Trim());q.Parameters.AddWithValue("$url",x.Url.Trim());q.Parameters.AddWithValue("$enabled",x.Enabled?1:0);}
}