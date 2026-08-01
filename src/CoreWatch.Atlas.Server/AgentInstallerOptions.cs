namespace CoreWatch.Atlas.Server;

public sealed class AgentInstallerOptions
{
    public const string SectionName = "Atlas:AgentInstaller";

    public string AgentPackagePath { get; set; } = "/downloads/corewatch-atlas-agent.zip";
}
// CoreWatch Atlas module: AgentInstallerOptions.
