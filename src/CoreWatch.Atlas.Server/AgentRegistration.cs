namespace CoreWatch.Atlas.Server;

public sealed record AgentRegistrationRequest(
    string RegistrationToken,
    string HostName,
    string OperatingSystem,
    string Architecture,
    string AgentVersion,
    Guid? ExistingAgentId = null);

public sealed record RegisteredAgent(
    Guid AgentId,
    DateTimeOffset RegisteredAtUtc,
    string Credential);

public sealed record IssuedRegistrationToken(
    string Value,
    DateTimeOffset ExpiresAtUtc);
