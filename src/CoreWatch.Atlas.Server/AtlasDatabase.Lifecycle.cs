using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<bool> ArchiveAgentAsync(
        Guid agentId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE agents
            SET archived_at_utc = $archivedAtUtc,
                credential_revoked_at_utc = COALESCE(
                    credential_revoked_at_utc,
                    $archivedAtUtc)
            WHERE agent_id = $agentId
              AND archived_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$archivedAtUtc", FormatTimestamp(now));
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await InsertLifecycleAuditAsync(
            connection,
            transaction,
            agentId,
            operatorId,
            "agent_archived",
            null,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RestoreAgentAsync(
        Guid agentId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE agents
            SET archived_at_utc = NULL
            WHERE agent_id = $agentId
              AND archived_at_utc IS NOT NULL;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await InsertLifecycleAuditAsync(
            connection,
            transaction,
            agentId,
            operatorId,
            "agent_restored",
            null,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAgentAsync(
        Guid agentId,
        Guid operatorId,
        bool deleteSnapshots,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        if (!deleteSnapshots)
        {
            await using var retainSnapshots = connection.CreateCommand();
            retainSnapshots.Transaction = transaction;
            retainSnapshots.CommandText =
                """
                UPDATE snapshots
                SET retained_agent_id = agent_id,
                    agent_id = NULL
                WHERE agent_id = $agentId;
                """;
            retainSnapshots.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            await retainSnapshots.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var deleteAgent = connection.CreateCommand();
        deleteAgent.Transaction = transaction;
        deleteAgent.CommandText = "DELETE FROM agents WHERE agent_id = $agentId;";
        deleteAgent.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        if (await deleteAgent.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await InsertLifecycleAuditAsync(
            connection,
            transaction,
            agentId,
            operatorId,
            deleteSnapshots
                ? "agent_deleted_snapshots_deleted"
                : "agent_deleted_snapshots_retained",
            !deleteSnapshots,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task InsertLifecycleAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid agentId,
        Guid operatorId,
        string eventType,
        bool? snapshotsRetained,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO agent_lifecycle_audit (
                agent_id,
                operator_id,
                occurred_at_utc,
                event_type,
                snapshots_retained)
            VALUES (
                $agentId,
                $operatorId,
                $occurredAtUtc,
                $eventType,
                $snapshotsRetained);
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$operatorId", operatorId.ToString("D"));
        command.Parameters.AddWithValue("$occurredAtUtc", FormatTimestamp(occurredAt));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue(
            "$snapshotsRetained",
            snapshotsRetained is null ? DBNull.Value : snapshotsRetained.Value ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}