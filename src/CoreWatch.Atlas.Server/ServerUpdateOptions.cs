namespace CoreWatch.Atlas.Server;

public sealed class ServerUpdateOptions
{
    public const string SectionName = "Atlas:ServerUpdate";

    public bool Enabled { get; set; }
    public string Version { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string StatePath { get; set; } = "data/updates";
    public int CheckIntervalMinutes { get; set; } = 360;
}

internal sealed record ServerUpdateHandoff(
    string TargetVersion,
    int ParentProcessId,
    string PackagePath,
    string InstallDirectory,
    string BackupDirectory,
    string Sha256);
// CoreWatch Atlas module: ServerUpdateOptions.
