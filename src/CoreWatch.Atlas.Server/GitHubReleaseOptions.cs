namespace CoreWatch.Atlas.Server;

public sealed class GitHubReleaseOptions
{
    public const string SectionName = "Atlas:GitHubRelease";

    public bool Enabled { get; set; } = true;
    public string Repository { get; set; } = "Wakbu/CoreWatch-Atlas";
    public int CacheMinutes { get; set; } = 15;
}

public sealed record PublishedRelease(string Version, string ServerPackageUrl, string ServerSha256, string AgentPackageUrl, string AgentSha256);
// CoreWatch Atlas module: GitHubReleaseOptions.
