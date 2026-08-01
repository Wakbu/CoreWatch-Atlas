using System.Globalization;
using System.Text.Json;
using CoreWatch.Atlas.Contracts;
using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SnapshotRecord> StoreSnapshotAsync(
        Guid agentId,
        SystemMetricsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var receivedAt = _timeProvider.GetUtcNow();
        var payload = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO snapshots (
                agent_id,
                captured_at_utc,
                received_at_utc,
                payload_json)
            VALUES (
                $agentId,
                $capturedAtUtc,
                $receivedAtUtc,
                $payloadJson);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        insert.Parameters.AddWithValue(
            "$capturedAtUtc",
            FormatTimestamp(snapshot.CapturedAtUtc));
        insert.Parameters.AddWithValue("$receivedAtUtc", FormatTimestamp(receivedAt));
        insert.Parameters.AddWithValue("$payloadJson", payload);
        var snapshotId = Convert.ToInt64(
            await insert.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE agents
            SET host_name = $hostName,
                operating_system = $operatingSystem,
                architecture = $architecture,
                agent_version = $agentVersion,
                last_seen_at_utc = $lastSeenAtUtc
            WHERE agent_id = $agentId;
            """;
        update.Parameters.AddWithValue("$hostName", snapshot.Agent.HostName);
        update.Parameters.AddWithValue(
            "$operatingSystem",
            snapshot.Agent.OperatingSystem);
        update.Parameters.AddWithValue("$architecture", snapshot.Agent.Architecture);
        update.Parameters.AddWithValue("$agentVersion", snapshot.Agent.AgentVersion);
        update.Parameters.AddWithValue("$lastSeenAtUtc", FormatTimestamp(receivedAt));
        update.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Registered agent no longer exists.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new SnapshotRecord(
            snapshotId,
            snapshot.CapturedAtUtc,
            receivedAt,
            ParseMetrics(payload));
    }

    public Task<IReadOnlyList<AgentSummary>> ListAgentsAsync(
        TimeSpan offlineAfter,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        ReadAgentsAsync(null, offlineAfter, includeArchived, cancellationToken);

    public async Task<AgentSummary?> GetAgentAsync(
        Guid agentId,
        TimeSpan offlineAfter,
        CancellationToken cancellationToken = default)
    {
        var agents = await ReadAgentsAsync(
            agentId,
            offlineAfter,
            includeArchived: true,
            cancellationToken);
        return agents.SingleOrDefault();
    }

    public async Task<IReadOnlyList<SnapshotRecord>> GetSnapshotsAsync(
        Guid agentId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, captured_at_utc, received_at_utc, payload_json
            FROM snapshots
            WHERE agent_id = $agentId
              AND captured_at_utc >= $fromUtc
              AND captured_at_utc <= $toUtc
            ORDER BY captured_at_utc DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$fromUtc", FormatTimestamp(fromUtc));
        command.Parameters.AddWithValue("$toUtc", FormatTimestamp(toUtc));
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var snapshots = new List<SnapshotRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(ReadSnapshot(reader, 0));
        }

        return snapshots;
    }

    public async Task<int> DeleteSnapshotsOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM snapshots WHERE received_at_utc < $cutoffUtc;";
        command.Parameters.AddWithValue("$cutoffUtc", FormatTimestamp(cutoffUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AgentSummary>> ReadAgentsAsync(
        Guid? agentId,
        TimeSpan offlineAfter,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                a.agent_id,
                a.host_name,
                a.operating_system,
                a.architecture,
                a.agent_version,
                a.registered_at_utc,
                a.last_seen_at_utc,
                a.archived_at_utc,
                s.id,
                s.captured_at_utc,
                s.received_at_utc,
                s.payload_json
            FROM agents AS a
            LEFT JOIN snapshots AS s
                ON s.id = (
                    SELECT latest.id
                    FROM snapshots AS latest
                    WHERE latest.agent_id = a.agent_id
                    ORDER BY latest.captured_at_utc DESC, latest.id DESC
                    LIMIT 1
                )
            WHERE ($agentId IS NULL OR a.agent_id = $agentId)
              AND ($includeArchived = 1 OR a.archived_at_utc IS NULL)
            ORDER BY a.archived_at_utc IS NOT NULL, a.host_name, a.agent_id;
            """;
        command.Parameters.AddWithValue(
            "$agentId",
            agentId is null ? DBNull.Value : agentId.Value.ToString("D"));
        command.Parameters.AddWithValue("$includeArchived", includeArchived ? 1 : 0);
        var onlineCutoff = _timeProvider.GetUtcNow().Subtract(offlineAfter);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var agents = new List<AgentSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? lastSeen =
                reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6));
            DateTimeOffset? archivedAt =
                reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7));
            var latest = reader.IsDBNull(8) ? null : ReadSnapshot(reader, 8);
            agents.Add(new AgentSummary(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseTimestamp(reader.GetString(5)),
                lastSeen,
                archivedAt is not null,
                archivedAt,
                archivedAt is null && lastSeen >= onlineCutoff,
                latest));
        }

        return agents;
    }

    private static SnapshotRecord ReadSnapshot(SqliteDataReader reader, int offset)
    {
        var payload = reader.GetString(offset + 3);
        return new SnapshotRecord(
            reader.GetInt64(offset),
            ParseTimestamp(reader.GetString(offset + 1)),
            ParseTimestamp(reader.GetString(offset + 2)),
            ParseMetrics(payload));
    }

    private static JsonElement ParseMetrics(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
}
// CoreWatch Atlas module: AtlasDatabase.Snapshots.
