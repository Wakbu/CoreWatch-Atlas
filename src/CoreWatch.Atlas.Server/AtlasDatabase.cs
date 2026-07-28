using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

public sealed class AtlasDatabase
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public AtlasDatabase(
        IOptions<ServerStorageOptions> options,
        IHostEnvironment environment)
    {
        var configuredPath = options.Value.DatabasePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                $"{ServerStorageOptions.SectionName}:DatabasePath must be configured.");
        }

        DatabasePath = Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath));

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        };
        _connectionString = builder.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(DatabasePath)
                    ?? throw new InvalidOperationException("Database directory is unavailable."));

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                """
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;
                """,
                cancellationToken);

            await using var transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS agents (
                    agent_id TEXT PRIMARY KEY,
                    host_name TEXT NOT NULL,
                    operating_system TEXT NOT NULL,
                    architecture TEXT NOT NULL,
                    agent_version TEXT NOT NULL,
                    registered_at_utc TEXT NOT NULL,
                    last_seen_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agent_id TEXT NOT NULL,
                    captured_at_utc TEXT NOT NULL,
                    received_at_utc TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    FOREIGN KEY (agent_id) REFERENCES agents(agent_id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_snapshots_agent_captured_at
                    ON snapshots (agent_id, captured_at_utc DESC);

                INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
                    VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
                """,
                cancellationToken,
                transaction);
            await transaction.CommitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<int> GetSchemaVersionAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
