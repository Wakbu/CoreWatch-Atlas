using System.Text.Json;

namespace CoreWatch.Atlas.Server;

public sealed record AgentCredentialResponse(
    Guid AgentId,
    string Credential,
    DateTimeOffset IssuedAtUtc);

public sealed record AgentSummary(
    Guid AgentId,
    string HostName,
    string OperatingSystem,
    string Architecture,
    string AgentVersion,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    bool Archived,
    DateTimeOffset? ArchivedAtUtc,
    bool Online,
    SnapshotRecord? LatestSnapshot);

public sealed record SnapshotRecord(
    long Id,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    JsonElement Metrics);

public sealed record AgentDeletionRequest(bool DeleteSnapshots);
