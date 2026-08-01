namespace CoreWatch.Atlas.Agent;

public sealed class ServerTransmissionOptions
{
    public const string SectionName = "Atlas:ServerTransmission";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:5000";

    public string AgentId { get; set; } = string.Empty;

    public string Credential { get; set; } = string.Empty;
}
// CoreWatch Atlas module: ServerTransmissionOptions.
