using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<bool> AuthenticateAgentAsync(
        Guid agentId,
        string? credential,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        var authenticated = false;
        if (!string.IsNullOrWhiteSpace(credential) && credential.Length <= 128)
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM agents
                WHERE agent_id = $agentId
                  AND credential_hash = $credentialHash
                  AND credential_revoked_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            command.Parameters.Add("$credentialHash", SqliteType.Blob).Value =
                HashToken(credential);
            authenticated =
                Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }

        if (!authenticated)
        {
            await WriteAuthenticationAuditAsync(
                agentId,
                "authentication_failed",
                remoteAddress,
                cancellationToken);
        }

        return authenticated;
    }

    public async Task<AgentCredentialResponse?> RotateCredentialAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        var issuedAt = _timeProvider.GetUtcNow();
        var credential = CreateAgentCredential();
        var credentialHash = HashToken(credential);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE agents
            SET credential_hash = $credentialHash,
                credential_created_at_utc = $createdAtUtc,
                credential_revoked_at_utc = NULL
            WHERE agent_id = $agentId
              AND credential_revoked_at_utc IS NULL;
            """;
        command.Parameters.Add("$credentialHash", SqliteType.Blob).Value = credentialHash;
        command.Parameters.AddWithValue("$createdAtUtc", FormatTimestamp(issuedAt));
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await InsertAuditAsync(
            connection,
            transaction,
            agentId,
            "credential_rotated",
            null,
            issuedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AgentCredentialResponse(agentId, credential, issuedAt);
    }

    public async Task<bool> RevokeCredentialAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        var revokedAt = _timeProvider.GetUtcNow();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE agents
            SET credential_revoked_at_utc = $revokedAtUtc
            WHERE agent_id = $agentId
              AND credential_revoked_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$revokedAtUtc", FormatTimestamp(revokedAt));
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await InsertAuditAsync(
            connection,
            transaction,
            agentId,
            "credential_revoked",
            null,
            revokedAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task WriteAuthenticationAuditAsync(
        Guid agentId,
        string eventType,
        string? remoteAddress,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO authentication_audit (
                agent_id,
                occurred_at_utc,
                event_type,
                remote_address)
            VALUES (
                $agentId,
                $occurredAtUtc,
                $eventType,
                $remoteAddress);
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue(
            "$occurredAtUtc",
            FormatTimestamp(_timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue(
            "$remoteAddress",
            (object?)remoteAddress ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid agentId,
        string eventType,
        string? remoteAddress,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO authentication_audit (
                agent_id,
                occurred_at_utc,
                event_type,
                remote_address)
            VALUES (
                $agentId,
                $occurredAtUtc,
                $eventType,
                $remoteAddress);
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$occurredAtUtc", FormatTimestamp(occurredAt));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue(
            "$remoteAddress",
            (object?)remoteAddress ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CreateAgentCredential() =>
        $"catlas_agent_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
}
// CoreWatch Atlas module: AtlasDatabase.Security.
