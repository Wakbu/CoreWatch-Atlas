using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    private static readonly HashSet<string> AgentUpdateStates =
        ["downloading", "staged", "applying", "succeeded", "failed", "rolled_back"];

    public async Task<AgentUpdateDeployment?> RequestAgentUpdateAsync(
        Guid agentId,
        Guid operatorId,
        AgentUpdateOptions release,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var cancel = connection.CreateCommand();
        cancel.Transaction = transaction;
        cancel.CommandText = """
            UPDATE agent_update_deployments
            SET state = 'failed', detail = 'Superseded by a newer operator request',
                updated_at_utc = $now
            WHERE agent_id = $agentId
              AND state IN ('pending','downloading','staged','applying');
            """;
        cancel.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        cancel.Parameters.AddWithValue("$now", FormatTimestamp(now));
        await cancel.ExecuteNonQueryAsync(cancellationToken);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO agent_update_deployments (
                agent_id, version, package_url, sha256, state, requested_by,
                requested_at_utc, updated_at_utc)
            SELECT $agentId, $version, $packageUrl, $sha256, 'pending',
                   $requestedBy, $now, $now
            WHERE EXISTS (
                SELECT 1 FROM agents
                WHERE agent_id = $agentId AND archived_at_utc IS NULL);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        insert.Parameters.AddWithValue("$version", release.Version);
        insert.Parameters.AddWithValue("$packageUrl", release.PackageUrl);
        insert.Parameters.AddWithValue("$sha256", release.Sha256.ToUpperInvariant());
        insert.Parameters.AddWithValue("$requestedBy", operatorId.ToString("D"));
        insert.Parameters.AddWithValue("$now", FormatTimestamp(now));
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (id == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return new AgentUpdateDeployment(
            id, agentId, release.Version, release.PackageUrl,
            release.Sha256.ToUpperInvariant(), "pending", null, now, now);
    }

    public async Task<AgentUpdateDeployment?> GetPendingAgentUpdateAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, agent_id, version, package_url, sha256, state, detail,
                   requested_at_utc, updated_at_utc
            FROM agent_update_deployments
            WHERE agent_id = $agentId
              AND state IN ('pending','downloading','staged','applying')
            ORDER BY id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAgentUpdate(reader) : null;
    }

    public async Task<IReadOnlyList<AgentUpdateDeployment>> ListAgentUpdatesAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, agent_id, version, package_url, sha256, state, detail,
                   requested_at_utc, updated_at_utc
            FROM agent_update_deployments
            WHERE agent_id = $agentId ORDER BY id DESC LIMIT 50;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AgentUpdateDeployment>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadAgentUpdate(reader));
        }
        return result;
    }

    public async Task<bool> UpdateAgentUpdateStatusAsync(
        Guid agentId,
        long deploymentId,
        string state,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        if (!AgentUpdateStates.Contains(state)
            || detail?.Length > 1000)
        {
            throw new ArgumentException("Invalid Agent update status.");
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE agent_update_deployments
            SET state = $state, detail = $detail, updated_at_utc = $now
            WHERE id = $id AND agent_id = $agentId
              AND (
                (state = 'pending' AND $state IN ('downloading','failed'))
                OR (state = 'downloading' AND $state IN ('staged','failed'))
                OR (state = 'staged' AND $state IN ('applying','failed'))
                OR (state = 'applying' AND $state IN (
                    'succeeded','failed','rolled_back'))
              );
            """;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", FormatTimestamp(now));
        command.Parameters.AddWithValue("$id", deploymentId);
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static AgentUpdateDeployment ReadAgentUpdate(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture));
}
