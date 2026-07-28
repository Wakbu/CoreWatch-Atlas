using System.ComponentModel.DataAnnotations;

namespace CoreWatch.Atlas.Server;

public static class OperatorRoles
{
    public const string Viewer = "Viewer";
    public const string Administrator = "Administrator";

    public static bool IsValid(string role) =>
        role is Viewer or Administrator;
}

public static class OperatorPolicies
{
    public const string View = "Atlas.View";
    public const string Admin = "Atlas.Admin";
}

public sealed class OperatorAuthenticationOptions
{
    public const string SectionName = "Atlas:OperatorAuthentication";

    public int SessionMinutes { get; set; } = 30;

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;
}

public sealed record OperatorAccount(
    Guid OperatorId,
    string Username,
    string PasswordHash,
    string Role,
    bool Enabled,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc);

public sealed record OperatorIdentity(
    Guid OperatorId,
    string Username,
    string Role);

public sealed record OperatorSummary(
    Guid OperatorId,
    string Username,
    string Role,
    bool Enabled,
    DateTimeOffset CreatedAtUtc);

public sealed record OperatorLoginRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Username,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password);

public sealed record OperatorSessionResponse(string Username, string Role);

public enum OperatorLoginStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
}

public sealed record OperatorLoginResult(
    OperatorLoginStatus Status,
    OperatorIdentity? Identity);
