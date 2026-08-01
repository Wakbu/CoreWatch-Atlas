namespace CoreWatch.Atlas.Server;

public static class AlertMetricTypes
{
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string Disk = "disk";
    public const string Offline = "offline";
}

public sealed record AlertRule(long Id, string Name, string MetricType, double Threshold, string Severity, bool Enabled, int DurationSeconds=0, int RenotifyMinutes=0, int EscalateMinutes=0, string? Assignee=null, Guid? TargetAgentId=null, long? TargetGroupId=null);
public sealed record AlertRuleRequest(string Name, string MetricType, double Threshold, string Severity, bool Enabled, int DurationSeconds=0, int RenotifyMinutes=0, int EscalateMinutes=0, string? Assignee=null, Guid? TargetAgentId=null, long? TargetGroupId=null);
public sealed record AlertRecord(long Id, Guid AgentId, string HostName, long RuleId, string RuleName, string MetricType, string Severity, double CurrentValue, string State, DateTimeOffset OpenedAtUtc, DateTimeOffset? ResolvedAtUtc, DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedBy, string? AssignedTo=null);
public sealed record AlertAction(long Id,long AlertId,string ActionType,string Actor,string? Note,string? Assignee,DateTimeOffset CreatedAtUtc);
public sealed record AlertActionRequest(string? Note,string? Assignee,bool Resolve=false);
public sealed record NotificationChannel(long Id, string Name, string Url, bool Enabled, string ChannelType="generic", string? Template=null);
public sealed record NotificationChannelRequest(string Name, string Url, bool Enabled, string ChannelType="generic", string? Template=null);
