using System.Security.Claims;
using CoreWatch.Atlas.Contracts;
using CoreWatch.Atlas.Server;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

const long maximumSnapshotBytes = 2 * 1024 * 1024;

var createOperatorUsername = ReadOption(args, "--create-operator");
var createOperatorRole =
    ReadOption(args, "--role") ?? OperatorRoles.Administrator;
var createRegistrationToken = args.Contains(
    "--create-registration-token",
    StringComparer.Ordinal);
if (createRegistrationToken && createOperatorUsername is not null)
{
    throw new InvalidOperationException(
        "Only one local administration command can run at a time.");
}

var builder = WebApplication.CreateBuilder(
    RemoveLocalCommandArguments(args));
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
builder.Services
    .AddOptions<OperatorAuthenticationOptions>()
    .Bind(
        builder.Configuration.GetSection(
            OperatorAuthenticationOptions.SectionName))
    .Validate(
        options => options.SessionMinutes is >= 5 and <= 1440,
        "SessionMinutes must be between 5 and 1440.")
    .Validate(
        options => options.MaxFailedAttempts is >= 3 and <= 20,
        "MaxFailedAttempts must be between 3 and 20.")
    .Validate(
        options => options.LockoutMinutes is >= 1 and <= 1440,
        "LockoutMinutes must be between 1 and 1440.")
    .ValidateOnStart();
builder.Services.AddAtlasServerSecurity(
    builder.Configuration,
    builder.Environment);
var sessionMinutes = builder.Configuration.GetValue(
    $"{OperatorAuthenticationOptions.SectionName}:SessionMinutes",
    30);
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(
        options =>
        {
            options.Cookie.Name = "CoreWatchAtlas.Operator";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(
                Math.Clamp(sessionMinutes, 5, 1440));
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
builder.Services.AddAuthorization(
    options =>
    {
        options.AddPolicy(
            OperatorPolicies.View,
            policy => policy.RequireRole(
                OperatorRoles.Viewer,
                OperatorRoles.Administrator));
        options.AddPolicy(
            OperatorPolicies.Admin,
            policy => policy.RequireRole(OperatorRoles.Administrator));
    });
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

if (createOperatorUsername is not null)
{
    var password = ReadConfirmedPassword();
    var created = await database.CreateOperatorAsync(
        createOperatorUsername,
        password,
        createOperatorRole);
    Console.WriteLine(
        $"Created operator '{created.Username}' with role '{created.Role}'.");
    return;
}

app.UseAtlasServerSecurity();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
    "/health/live",
    () => Results.Ok(new { status = "ok" }));

app.MapGet(
    "/api/v1/auth/csrf",
    (HttpContext context, IAntiforgery antiforgery) =>
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { token = tokens.RequestToken });
    });

app.MapPost(
    "/api/v1/auth/login",
    async (
        OperatorLoginRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        AtlasDatabase storage,
        IOptions<OperatorAuthenticationOptions> options,
        CancellationToken cancellationToken) =>
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid CSRF token." });
        }

        var settings = options.Value;
        var result = await storage.AuthenticateOperatorAsync(
            request.Username,
            request.Password,
            context.Connection.RemoteIpAddress?.ToString(),
            settings.MaxFailedAttempts,
            TimeSpan.FromMinutes(settings.LockoutMinutes),
            cancellationToken);
        if (result.Status != OperatorLoginStatus.Succeeded
            || result.Identity is null)
        {
            return Results.Unauthorized();
        }

        var identity = result.Identity;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.OperatorId.ToString("D")),
            new Claim(ClaimTypes.Name, identity.Username),
            new Claim(ClaimTypes.Role, identity.Role),
        };
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme)));
        return Results.Ok(
            new OperatorSessionResponse(identity.Username, identity.Role));
    });

app.MapGet(
        "/api/v1/auth/me",
        (ClaimsPrincipal user) => Results.Ok(
            new OperatorSessionResponse(
                user.Identity?.Name ?? string.Empty,
                user.FindFirstValue(ClaimTypes.Role) ?? string.Empty)))
    .RequireAuthorization(OperatorPolicies.View);

app.MapGet(
        "/api/v1/operators",
        async (
            AtlasDatabase storage,
            CancellationToken cancellationToken) =>
            Results.Ok(
                await storage.ListOperatorsAsync(cancellationToken)))
    .RequireAuthorization(OperatorPolicies.Admin);

app.MapPost(
        "/api/v1/auth/logout",
        async (
            ClaimsPrincipal user,
            HttpContext context,
            IAntiforgery antiforgery,
            AtlasDatabase storage,
            CancellationToken cancellationToken) =>
        {
            if (!await ValidateAntiforgeryAsync(context, antiforgery))
            {
                return Results.BadRequest(new { error = "Invalid CSRF token." });
            }

            if (Guid.TryParse(
                    user.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var operatorId))
            {
                await storage.WriteOperatorEventAsync(
                    operatorId,
                    "logout",
                    context.Connection.RemoteIpAddress?.ToString(),
                    cancellationToken);
            }

            await context.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
    .RequireAuthorization(OperatorPolicies.View);

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
    })
    .RequireAuthorization(OperatorPolicies.View);

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
    })
    .RequireAuthorization(OperatorPolicies.View);

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
    })
    .RequireAuthorization(OperatorPolicies.View);

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
    })
    .RequireAuthorization(OperatorPolicies.View);

app.Run();

static async Task<bool> ValidateAntiforgeryAsync(
    HttpContext context,
    IAntiforgery antiforgery)
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        return true;
    }
    catch (AntiforgeryValidationException)
    {
        return false;
    }
}

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

static string? ReadOption(string[] arguments, string option)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (string.Equals(arguments[index], option, StringComparison.Ordinal))
        {
            if (index + 1 >= arguments.Length
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{option} requires a value.");
            }

            return arguments[index + 1];
        }
    }

    return null;
}

static string[] RemoveLocalCommandArguments(string[] arguments)
{
    var result = new List<string>();
    for (var index = 0; index < arguments.Length; index++)
    {
        if (string.Equals(
                arguments[index],
                "--create-registration-token",
                StringComparison.Ordinal))
        {
            continue;
        }

        if (arguments[index] is "--create-operator" or "--role")
        {
            index++;
            continue;
        }

        result.Add(arguments[index]);
    }

    return result.ToArray();
}

static string ReadPassword(string prompt)
{
    if (Console.IsInputRedirected)
    {
        throw new InvalidOperationException(
            "Operator creation requires an interactive terminal.");
    }

    Console.Write(prompt);
    var value = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string(value.ToArray());
        }

        if (key.Key == ConsoleKey.Backspace && value.Count > 0)
        {
            value.RemoveAt(value.Count - 1);
        }
        else if (!char.IsControl(key.KeyChar))
        {
            value.Add(key.KeyChar);
        }
    }
}

static string ReadConfirmedPassword()
{
    while (true)
    {
        var password = ReadPassword("Password: ");
        if (password.Length is < 12 or > 128)
        {
            Console.Error.WriteLine(
                "Password must contain between 12 and 128 characters. Try again.");
            continue;
        }

        var confirmation = ReadPassword("Confirm password: ");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Password confirmation does not match. Try again.");
            continue;
        }

        return password;
    }
}

public partial class Program;
