using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string definition,
        CancellationToken cancellationToken,
        string tableName = "agents")
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        if (!new[] { "agents", "asset_metadata", "alert_rules", "alerts", "notification_channels", "maintenance_windows" }.Contains(tableName, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(tableName));
        query.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        await using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateSnapshotsForRetainedHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "PRAGMA table_info(snapshots);";
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        var hasRetainedAgentId = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), "retained_agent_id", StringComparison.Ordinal))
            {
                hasRetainedAgentId = true;
                break;
            }
        }

        if (hasRetainedAgentId)
        {
            return;
        }

        await reader.DisposeAsync();
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE snapshots_v5 (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_id TEXT NULL,
                retained_agent_id TEXT NULL,
                captured_at_utc TEXT NOT NULL,
                received_at_utc TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                FOREIGN KEY (agent_id) REFERENCES agents(agent_id) ON DELETE CASCADE
            );

            INSERT INTO snapshots_v5 (
                id,
                agent_id,
                retained_agent_id,
                captured_at_utc,
                received_at_utc,
                payload_json)
            SELECT
                id,
                agent_id,
                NULL,
                captured_at_utc,
                received_at_utc,
                payload_json
            FROM snapshots;

            DROP TABLE snapshots;
            ALTER TABLE snapshots_v5 RENAME TO snapshots;
            CREATE INDEX ix_snapshots_agent_captured_at
                ON snapshots (agent_id, captured_at_utc DESC);
            """,
            cancellationToken,
            transaction);
    }
}
// CoreWatch Atlas module: AtlasDatabase.Migrations.
