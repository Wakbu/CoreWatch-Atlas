namespace CoreWatch.Atlas.Server;

public sealed class ServerApiOptions
{
    public const string SectionName = "Atlas:ServerApi";

    public int OfflineAfterSeconds { get; set; } = 45;

    public int SnapshotRetentionDays { get; set; } = 30;

    public int CleanupIntervalMinutes { get; set; } = 60;
}
