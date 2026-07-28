using CoreWatch.Atlas.Contracts;
using CoreWatch.Atlas.Server;
using Microsoft.Extensions.Options;

const long maximumSnapshotBytes = 2 * 1024 * 1024;

var createRegistrationToken = args.Contains(
    "--create-registration-token",
    StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(
    args.Where(
            argument => !string.Equals(
                argument,
                "--create-registration-token",
                StringComparison.Ordinal))
        .ToArray());
builder.WebHost.ConfigureKestrel(
    options => options.Limits.MaxRequestBodySize = maximumSnapshotBytes);
builder.Services
    .AddOptions<ServerStorageOptions>()
    .Bind(builder.Configuration.GetSection(ServerStorageOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DatabasePath),
        "DatabasePath must not be empty.")
    .ValidateOnStart();
builder.Services
    .AddOptions<RegistrationOptions>()
    .Bind(builder.Configuration.GetSection(RegistrationOptions.SectionName))
    .Validate(
        options => options.TokenLifetimeMinutes is >= 1 and <= 1440,
        "TokenLifetimeMinutes must be between 1 and 1440.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ServerApiOptions>()
    .Bind(builder.Configuration.GetSection(ServerApiOptions.SectionName))
    .Validate(
        options => options.OfflineAfterSeconds is >= 15 and <= 86400,
        "OfflineAfterSeconds must be between 15 and 86400.")
    .Validate(
        options => options.SnapshotRetentionDays is >= 1 and <= 3650,
        "SnapshotRetentionDays must be between 1 and 3650.")
    .Validate(
        options => options.CleanupIntervalMinutes is >= 1 and <= 1440,
        "CleanupIntervalMinutes must be between 1 and 1440.")
    .ValidateOnStart();
builder.Services.AddSingleton<AtlasDatabase>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<SnapshotRetentionWorker>();

var app = builder.Build();
var database = app.Services.GetRequiredService<AtlasDatabase>();
await database.InitializeAsync();

if (createRegistrationToken)
{
    var options = app.Services.GetRequiredService<IOptions<RegistrationOptions>>().Value;
    var issuedToken = await database.CreateRegistrationTokenAsync(
        TimeSpan.FromMinutes(options.TokenLifetimeMinutes));
    Console.WriteLine(issuedToken.Value);
    Console.WriteLine($"Expires at (UTC): {issuedToken.ExpiresAtUtc:O}");
    return;
}

app.MapGet(
    "/health/live",
    () => Results.Ok(new { status = "ok" }));

app.MapGet(
    "/health/ready",
    async (AtlasDatabase storage, CancellationToken cancellationToken) =>
    {
        var schemaVersion = await storage.GetSchemaVersionAsync(cancellationToken);
        return schemaVersion == AtlasDatabase.CurrentSchemaVersion
            ? Results.Ok(new
            {
                status = "ready",
                storage = "sqlite",
                schemaVersion,
            })
            : Results.Json(
                new
                {
                    status = "not-ready",
                    storage = "sqlite",
                    schemaVersion,
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });

app.MapGet(
    "/api/v1/status",
    async (
        AtlasDatabase storage,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var schemaVersion = await storage.GetSchemaVersionAsync(cancellationToken);
        return Results.Ok(new
        {
            service = "CoreWatch-Atlas.Server",
            status = "running",
            version = typeof(Program).Assembly.GetName().Version?.ToString(),
            timestampUtc = timeProvider.GetUtcNow(),
            storage = new
            {
                provider = "sqlite",
                schemaVersion,
            },
        });
    });

app.MapPost(
    "/api/v1/agents/register",
    async (
        AgentRegistrationRequest request,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        var validationErrors = ValidateRegistration(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var registeredAgent = await storage.RegisterAgentAsync(
            request,
            cancellationToken);
        return registeredAgent is null
            ? Results.Unauthorized()
            : Results.Created(
                $"/api/v1/agents/{registeredAgent.AgentId:D}",
                registeredAgent);
    });

app.MapPost(
    "/api/v1/agents/{agentId:guid}/credentials/rotate",
    async (
        Guid agentId,
        HttpContext context,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        if (!await AuthenticateAsync(context, storage, agentId, cancellationToken))
        {
            return Results.Unauthorized();
        }

        var credential = await storage.RotateCredentialAsync(agentId, cancellationToken);
        return credential is null ? Results.NotFound() : Results.Ok(credential);
    });

app.MapDelete(
    "/api/v1/agents/{agentId:guid}/credentials",
    async (
        Guid agentId,
        HttpContext context,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        if (!await AuthenticateAsync(context, storage, agentId, cancellationToken))
        {
            return Results.Unauthorized();
        }

        return await storage.RevokeCredentialAsync(agentId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    });

app.MapPost(
    "/api/v1/agents/{agentId:guid}/snapshots",
    async (
        Guid agentId,
        HttpContext context,
        SnapshotUploadRequest request,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        if (!await AuthenticateAsync(context, storage, agentId, cancellationToken))
        {
            return Results.Unauthorized();
        }

        if (context.Request.ContentLength > maximumSnapshotBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        SystemMetricsSnapshot snapshot;
        try
        {
            snapshot = request.ToContract();
        }
        catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
        {
            return Results.BadRequest(new
            {
                error = "Snapshot metrics failed contract validation.",
            });
        }

        if (!string.Equals(
                snapshot.Agent.AgentId,
                agentId.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = "Snapshot AgentId must match the authenticated route AgentId.",
            });
        }

        var stored = await storage.StoreSnapshotAsync(
            agentId,
            snapshot,
            cancellationToken);
        return Results.Created(
            $"/api/v1/agents/{agentId:D}/snapshots/{stored.Id}",
            stored);
    });

app.MapGet(
    "/api/v1/agents",
    async (
        AtlasDatabase storage,
        IOptions<ServerApiOptions> options,
        CancellationToken cancellationToken) =>
    {
        var offlineAfter = TimeSpan.FromSeconds(options.Value.OfflineAfterSeconds);
        return Results.Ok(
            await storage.ListAgentsAsync(offlineAfter, cancellationToken));
    });

app.MapGet(
    "/api/v1/agents/{agentId:guid}",
    async (
        Guid agentId,
        AtlasDatabase storage,
        IOptions<ServerApiOptions> options,
        CancellationToken cancellationToken) =>
    {
        var offlineAfter = TimeSpan.FromSeconds(options.Value.OfflineAfterSeconds);
        var agent = await storage.GetAgentAsync(
            agentId,
            offlineAfter,
            cancellationToken);
        return agent is null ? Results.NotFound() : Results.Ok(agent);
    });

app.MapGet(
    "/api/v1/agents/{agentId:guid}/snapshots",
    async (
        Guid agentId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        AtlasDatabase storage,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var to = (toUtc ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var from = (fromUtc ?? to.AddHours(-24)).ToUniversalTime();
        var requestedLimit = limit ?? 200;
        if (from > to || requestedLimit is < 1 or > 1000)
        {
            return Results.BadRequest(new
            {
                error = "fromUtc must not exceed toUtc and limit must be from 1 through 1000.",
            });
        }

        if (await storage.GetAgentAsync(
                agentId,
                TimeSpan.FromDays(1),
                cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            await storage.GetSnapshotsAsync(
                agentId,
                from,
                to,
                requestedLimit,
                cancellationToken));
    });

app.Run();

static async Task<bool> AuthenticateAsync(
    HttpContext context,
    AtlasDatabase database,
    Guid agentId,
    CancellationToken cancellationToken)
{
    const string bearerPrefix = "Bearer ";
    var authorization = context.Request.Headers.Authorization.ToString();
    var credential = authorization.StartsWith(
        bearerPrefix,
        StringComparison.OrdinalIgnoreCase)
        ? authorization[bearerPrefix.Length..]
        : null;
    return await database.AuthenticateAgentAsync(
        agentId,
        credential,
        context.Connection.RemoteIpAddress?.ToString(),
        cancellationToken);
}

static Dictionary<string, string[]> ValidateRegistration(
    AgentRegistrationRequest request)
{
    var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    AddLengthError(
        errors,
        nameof(request.RegistrationToken),
        request.RegistrationToken,
        1,
        128);
    AddLengthError(errors, nameof(request.HostName), request.HostName, 1, 255);
    AddLengthError(
        errors,
        nameof(request.OperatingSystem),
        request.OperatingSystem,
        1,
        128);
    AddLengthError(errors, nameof(request.Architecture), request.Architecture, 1, 64);
    AddLengthError(errors, nameof(request.AgentVersion), request.AgentVersion, 1, 64);
    return errors;
}

static void AddLengthError(
    Dictionary<string, string[]> errors,
    string field,
    string? value,
    int minimum,
    int maximum)
{
    if (string.IsNullOrWhiteSpace(value) || value.Length < minimum || value.Length > maximum)
    {
        errors[field] =
            [$"{field} must contain between {minimum} and {maximum} characters."];
    }
}

public partial class Program;
