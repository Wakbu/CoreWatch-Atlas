namespace CoreWatch.Atlas.Server;

public static class AlertMetricTypes
{
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string Disk = "disk";
    public const string Offline = "offline";
}

public sealed record AlertRule(long Id, string Name, string MetricType, double Threshold, string Severity, bool Enabled);
public sealed record AlertRuleRequest(string Name, string MetricType, double Threshold, string Severity, bool Enabled);
public sealed record AlertRecord(long Id, Guid AgentId, string HostName, long RuleId, string RuleName, string MetricType, string Severity, double CurrentValue, string State, DateTimeOffset OpenedAtUtc, DateTimeOffset? ResolvedAtUtc, DateTimeOffset? AcknowledgedAtUtc, string? AcknowledgedBy);
public sealed record NotificationChannel(long Id, string Name, string Url, bool Enabled);
public sealed record NotificationChannelRequest(string Name, string Url, bool Enabled);