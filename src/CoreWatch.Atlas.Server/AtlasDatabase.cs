using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

public sealed class AtlasDatabase
{
    public const int CurrentSchemaVersion = 2;

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public AtlasDatabase(
        IOptions<ServerStorageOptions> options,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
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
            DefaultTimeout = 5,
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

                CREATE TABLE IF NOT EXISTS registration_tokens (
                    token_hash BLOB PRIMARY KEY,
                    created_at_utc TEXT NOT NULL,
                    expires_at_utc TEXT NOT NULL,
                    consumed_at_utc TEXT NULL,
                    consumed_agent_id TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_registration_tokens_expires_at
                    ON registration_tokens (expires_at_utc);

                INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc)
                    VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
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
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public async Task<IssuedRegistrationToken> CreateRegistrationTokenAsync(
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "Token lifetime must be positive.");
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(lifetime);
        var tokenValue =
            $"catlas_reg_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
        var tokenHash = HashToken(tokenValue);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO registration_tokens (
                token_hash,
                created_at_utc,
                expires_at_utc)
            VALUES (
                $tokenHash,
                $createdAtUtc,
                $expiresAtUtc);
            """;
        command.Parameters.Add("$tokenHash", SqliteType.Blob).Value = tokenHash;
        command.Parameters.AddWithValue("$createdAtUtc", FormatTimestamp(now));
        command.Parameters.AddWithValue("$expiresAtUtc", FormatTimestamp(expiresAt));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new IssuedRegistrationToken(tokenValue, expiresAt);
    }

    public async Task<RegisteredAgent?> RegisterAgentAsync(
        AgentRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var agentId = Guid.CreateVersion7(now);
        var tokenHash = HashToken(request.RegistrationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var consumeCommand = connection.CreateCommand();
        consumeCommand.Transaction = transaction;
        consumeCommand.CommandText =
            """
            UPDATE registration_tokens
            SET consumed_at_utc = $consumedAtUtc,
                consumed_agent_id = $agentId
            WHERE token_hash = $tokenHash
              AND consumed_at_utc IS NULL
              AND expires_at_utc > $consumedAtUtc;
            """;
        consumeCommand.Parameters.AddWithValue("$consumedAtUtc", FormatTimestamp(now));
        consumeCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        consumeCommand.Parameters.Add("$tokenHash", SqliteType.Blob).Value = tokenHash;
        var consumed = await consumeCommand.ExecuteNonQueryAsync(cancellationToken);
        if (consumed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO agents (
                agent_id,
                host_name,
                operating_system,
                architecture,
                agent_version,
                registered_at_utc)
            VALUES (
                $agentId,
                $hostName,
                $operatingSystem,
                $architecture,
                $agentVersion,
                $registeredAtUtc);
            """;
        insertCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        insertCommand.Parameters.AddWithValue("$hostName", request.HostName);
        insertCommand.Parameters.AddWithValue(
            "$operatingSystem",
            request.OperatingSystem);
        insertCommand.Parameters.AddWithValue("$architecture", request.Architecture);
        insertCommand.Parameters.AddWithValue("$agentVersion", request.AgentVersion);
        insertCommand.Parameters.AddWithValue("$registeredAtUtc", FormatTimestamp(now));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new RegisteredAgent(agentId, now);
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

    private static byte[] HashToken(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
