namespace CoreWatch.Atlas.Agent;

public sealed class MetricsCollectionOptions
{
    public const string SectionName = "Atlas:MetricsCollection";

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
}
