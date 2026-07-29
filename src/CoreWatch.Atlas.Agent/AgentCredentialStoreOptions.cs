namespace CoreWatch.Atlas.Agent;

public sealed class AgentCredentialStoreOptions
{
    public const string SectionName = "Atlas:CredentialStore";

    public string Path { get; set; } = "data/agent-credentials";
}
