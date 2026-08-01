using System.Text.Json;

namespace CoreWatch.Atlas.Server;

public sealed record AgentDiagnosticsConfiguration(string[] Services,string[] Processes,string[] Containers,string[] Urls,string[] TcpEndpoints,string[] PingTargets,string[] BackupPaths);

public sealed partial class AtlasDatabase
{
    private static readonly AgentDiagnosticsConfiguration EmptyDiagnostics=new([],[],[],[],[],[],[]);
    public async Task<AgentDiagnosticsConfiguration> GetAgentDiagnosticsConfigurationAsync(Guid id,CancellationToken ct=default)
    { await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT payload_json FROM agent_diagnostic_config WHERE agent_id=$id;";q.Parameters.AddWithValue("$id",id.ToString("D"));var json=await q.ExecuteScalarAsync(ct) as string;return json is null?EmptyDiagnostics:JsonSerializer.Deserialize<AgentDiagnosticsConfiguration>(json)??EmptyDiagnostics; }
    public async Task SetAgentDiagnosticsConfigurationAsync(Guid id,AgentDiagnosticsConfiguration x,CancellationToken ct=default)
    { ValidateDiagnostics(x);await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT INTO agent_diagnostic_config(agent_id,payload_json,updated_at_utc) VALUES($id,$json,$now) ON CONFLICT(agent_id) DO UPDATE SET payload_json=excluded.payload_json,updated_at_utc=excluded.updated_at_utc;";q.Parameters.AddWithValue("$id",id.ToString("D"));q.Parameters.AddWithValue("$json",JsonSerializer.Serialize(x));q.Parameters.AddWithValue("$now",FormatTimestamp(_timeProvider.GetUtcNow()));await q.ExecuteNonQueryAsync(ct); }
    private static void ValidateDiagnostics(AgentDiagnosticsConfiguration x){foreach(var values in new[]{x.Services,x.Processes,x.Containers,x.Urls,x.TcpEndpoints,x.PingTargets,x.BackupPaths})if(values.Length>50||values.Any(v=>string.IsNullOrWhiteSpace(v)||v.Length>300))throw new ArgumentException("Invalid diagnostics configuration.");if(x.Urls.Any(v=>!Uri.TryCreate(v,UriKind.Absolute,out var u)||u.Scheme!="https"))throw new ArgumentException("Diagnostic URLs must use HTTPS.");}
}
// CoreWatch Atlas module: AtlasDatabase.Diagnostics.
