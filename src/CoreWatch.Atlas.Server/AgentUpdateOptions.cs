namespace CoreWatch.Atlas.Server;

public sealed class AgentUpdateOptions
{
    public const string SectionName = "Atlas:AgentUpdate";
    public bool Enabled { get; set; }
    public string Version { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed record AgentUpdateManifest(long DeploymentId, string Version, string PackageUrl, string Sha256);

public sealed record AgentUpdateStatusRequest(string State, string? Detail);

public sealed record AgentUpdateDeployment(
    long Id,
    Guid AgentId,
    string Version,
    string PackageUrl,
    string Sha256,
    string State,
    string? Detail,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset UpdatedAtUtc);
// CoreWatch Atlas module: AgentUpdateOptions.
