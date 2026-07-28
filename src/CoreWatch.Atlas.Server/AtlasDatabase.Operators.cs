using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<OperatorIdentity> CreateOperatorAsync(
        string username,
        string password,
        string role,
        CancellationToken cancellationToken = default)
    {
        ValidateOperatorInput(username, password, role);
        var now = _timeProvider.GetUtcNow();
        var account = new OperatorAccount(
            Guid.CreateVersion7(now),
            username,
            string.Empty,
            role,
            true,
            0,
            null);
        var passwordHash = new PasswordHasher<OperatorAccount>().HashPassword(
            account,
            password);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO atlas_operators (
                operator_id,
                username,
                password_hash,
                role,
                enabled,
                failed_login_count,
                created_at_utc)
            VALUES (
                $operatorId,
                $username,
                $passwordHash,
                $role,
                1,
                0,
                $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$operatorId", account.OperatorId.ToString("D"));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$createdAtUtc", FormatTimestamp(now));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception)
            when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "An operator with that username already exists.",
                exception);
        }

        return new OperatorIdentity(account.OperatorId, username, role);
    }

    public async Task<IReadOnlyList<OperatorSummary>> ListOperatorsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT operator_id, username, role, enabled, created_at_utc
            FROM atlas_operators
            ORDER BY username COLLATE NOCASE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var operators = new List<OperatorSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            operators.Add(
                new OperatorSummary(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) == 1,
                    DateTimeOffset.Parse(
                        reader.GetString(4),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind)));
        }

        return operators;
    }

    public async Task<OperatorLoginResult> AuthenticateOperatorAsync(
        string? username,
        string? password,
        string? remoteAddress,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrEmpty(password)
            || username.Length > 64
            || password.Length > 128)
        {
            await WriteOperatorAuditAsync(
                null,
                "login_failed",
                remoteAddress,
                cancellationToken);
            return new OperatorLoginResult(
                OperatorLoginStatus.InvalidCredentials,
                null);
        }

        var now = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var account = await ReadOperatorAsync(
            connection,
            transaction,
            username,
            cancellationToken);
        if (account is null || !account.Enabled)
        {
            await InsertOperatorAuditAsync(
                connection,
                transaction,
                account?.OperatorId,
                "login_failed",
                remoteAddress,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OperatorLoginResult(
                OperatorLoginStatus.InvalidCredentials,
                null);
        }

        if (account.LockoutEndUtc > now)
        {
            await InsertOperatorAuditAsync(
                connection,
                transaction,
                account.OperatorId,
                "login_locked_out",
                remoteAddress,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OperatorLoginResult(OperatorLoginStatus.LockedOut, null);
        }

        var verification = new PasswordHasher<OperatorAccount>().VerifyHashedPassword(
            account,
            account.PasswordHash,
            password);
        if (verification == PasswordVerificationResult.Failed)
        {
            var previousFailures =
                account.LockoutEndUtc is not null && account.LockoutEndUtc <= now
                    ? 0
                    : account.FailedLoginCount;
            var failures = previousFailures + 1;
            var lockoutEnd = failures >= maxFailedAttempts
                ? now.Add(lockoutDuration)
                : (DateTimeOffset?)null;
            await UpdateLoginStateAsync(
                connection,
                transaction,
                account.OperatorId,
                failures,
                lockoutEnd,
                account.PasswordHash,
                cancellationToken);
            await InsertOperatorAuditAsync(
                connection,
                transaction,
                account.OperatorId,
                lockoutEnd is null ? "login_failed" : "login_locked_out",
                remoteAddress,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OperatorLoginResult(
                lockoutEnd is null
                    ? OperatorLoginStatus.InvalidCredentials
                    : OperatorLoginStatus.LockedOut,
                null);
        }

        var updatedHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? new PasswordHasher<OperatorAccount>().HashPassword(account, password)
            : account.PasswordHash;
        await UpdateLoginStateAsync(
            connection,
            transaction,
            account.OperatorId,
            0,
            null,
            updatedHash,
            cancellationToken);
        await InsertOperatorAuditAsync(
            connection,
            transaction,
            account.OperatorId,
            "login_succeeded",
            remoteAddress,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OperatorLoginResult(
            OperatorLoginStatus.Succeeded,
            new OperatorIdentity(account.OperatorId, account.Username, account.Role));
    }

    public async Task WriteOperatorEventAsync(
        Guid operatorId,
        string eventType,
        string? remoteAddress,
        CancellationToken cancellationToken = default) =>
        await WriteOperatorAuditAsync(
            operatorId,
            eventType,
            remoteAddress,
            cancellationToken);

    private static void ValidateOperatorInput(
        string username,
        string password,
        string role)
    {
        if (username.Length is < 3 or > 64
            || username.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException(
                "Username must contain 3-64 ASCII letters, digits, '.', '_' or '-'.",
                nameof(username));
        }

        if (password.Length is < 12 or > 128)
        {
            throw new ArgumentException(
                "Password must contain between 12 and 128 characters.",
                nameof(password));
        }

        if (!OperatorRoles.IsValid(role))
        {
            throw new ArgumentException("Role must be Viewer or Administrator.", nameof(role));
        }
    }

    private static async Task<OperatorAccount?> ReadOperatorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string username,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                operator_id,
                username,
                password_hash,
                role,
                enabled,
                failed_login_count,
                lockout_end_utc
            FROM atlas_operators
            WHERE username = $username COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$username", username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OperatorAccount(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4) == 1,
            reader.GetInt32(5),
            reader.IsDBNull(6)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(6),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));
    }

    private static async Task UpdateLoginStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operatorId,
        int failedLoginCount,
        DateTimeOffset? lockoutEnd,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE atlas_operators
            SET failed_login_count = $failedLoginCount,
                lockout_end_utc = $lockoutEndUtc,
                password_hash = $passwordHash
            WHERE operator_id = $operatorId;
            """;
        command.Parameters.AddWithValue("$failedLoginCount", failedLoginCount);
        command.Parameters.AddWithValue(
            "$lockoutEndUtc",
            lockoutEnd is null ? DBNull.Value : FormatTimestamp(lockoutEnd.Value));
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$operatorId", operatorId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteOperatorAuditAsync(
        Guid? operatorId,
        string eventType,
        string? remoteAddress,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO operator_authentication_audit (
                operator_id,
                occurred_at_utc,
                event_type,
                remote_address)
            VALUES (
                $operatorId,
                $occurredAtUtc,
                $eventType,
                $remoteAddress);
            """;
        command.Parameters.AddWithValue(
            "$operatorId",
            operatorId is null ? DBNull.Value : operatorId.Value.ToString("D"));
        command.Parameters.AddWithValue(
            "$occurredAtUtc",
            FormatTimestamp(_timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue(
            "$remoteAddress",
            (object?)remoteAddress ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOperatorAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid? operatorId,
        string eventType,
        string? remoteAddress,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO operator_authentication_audit (
                operator_id,
                occurred_at_utc,
                event_type,
                remote_address)
            VALUES (
                $operatorId,
                $occurredAtUtc,
                $eventType,
                $remoteAddress);
            """;
        command.Parameters.AddWithValue(
            "$operatorId",
            operatorId is null ? DBNull.Value : operatorId.Value.ToString("D"));
        command.Parameters.AddWithValue("$occurredAtUtc", FormatTimestamp(occurredAt));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue(
            "$remoteAddress",
            (object?)remoteAddress ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
