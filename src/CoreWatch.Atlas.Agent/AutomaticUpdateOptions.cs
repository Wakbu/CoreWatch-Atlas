namespace CoreWatch.Atlas.Agent;

public sealed class AutomaticUpdateOptions
{
    public const string SectionName = "Atlas:AutomaticUpdate";
    public bool Enabled { get; set; } = true;
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(15);
    public string StatePath { get; set; } = "data/updates";
    public string ServiceName { get; set; } = "";
}

internal sealed record AgentUpdateManifest(
    long DeploymentId,
    string Version,
    string PackageUrl,
    string Sha256);

internal sealed record AgentUpdateStatusRequest(string State, string? Detail);

internal sealed record AgentUpdateHandoff(
    long DeploymentId,
    string TargetVersion,
    int ParentProcessId,
    string PackagePath,
    string InstallDirectory,
    string BackupDirectory,
    string ServiceName,
    string ResultPath);

internal sealed record AgentUpdateResult(
    long DeploymentId,
    string TargetVersion,
    string State,
    string? Detail);
// CoreWatch Atlas module: AutomaticUpdateOptions.
