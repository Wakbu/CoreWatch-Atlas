using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CoreWatch.Atlas.Contracts;
using CoreWatch.Atlas.Server;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

const long maximumSnapshotBytes = 2 * 1024 * 1024;

var applyServerUpdate = ReadOption(args, "--apply-server-update");
if (applyServerUpdate is not null)
{
    Environment.ExitCode = await ServerUpdateInstaller.RunAsync(applyServerUpdate);
    return;
}

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
    .AddOptions<AgentInstallerOptions>()
    .Bind(builder.Configuration.GetSection(AgentInstallerOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(options.AgentPackagePath, UriKind.Relative, out var path)
            && path.OriginalString.StartsWith("/", StringComparison.Ordinal),
        "AgentPackagePath must be an absolute application path.")
    .ValidateOnStart();
builder.Services
    .AddOptions<AgentUpdateOptions>()
    .Bind(builder.Configuration.GetSection(AgentUpdateOptions.SectionName))
    .Validate(
        options => !options.Enabled || (Version.TryParse(options.Version, out _)
            && Uri.TryCreate(options.PackageUrl, UriKind.Absolute, out var packageUri)
            && (packageUri.Scheme == Uri.UriSchemeHttps || packageUri.IsLoopback)
            && options.Sha256.Length == 64
            && options.Sha256.All(Uri.IsHexDigit)),
        "Enabled AgentUpdate requires Version, absolute PackageUrl and SHA-256.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ServerUpdateOptions>()
    .Bind(builder.Configuration.GetSection(ServerUpdateOptions.SectionName))
    .Validate(
        options => options.CheckIntervalMinutes is >= 5 and <= 1440,
        "ServerUpdate CheckIntervalMinutes must be between 5 and 1440.")
    .Validate(
        options => !options.Enabled || (Version.TryParse(options.Version, out _)
            && Uri.TryCreate(options.PackageUrl, UriKind.Absolute, out var packageUri)
            && (packageUri.Scheme == Uri.UriSchemeHttps || packageUri.IsLoopback)
            && options.Sha256.Length == 64
            && options.Sha256.All(Uri.IsHexDigit)),
        "Enabled ServerUpdate requires Version, absolute PackageUrl and SHA-256.")
    .ValidateOnStart();
builder.Services
    .AddOptions<GitHubReleaseOptions>()
    .Bind(builder.Configuration.GetSection(GitHubReleaseOptions.SectionName))
    .Validate(options => options.CacheMinutes is >= 5 and <= 1440,
        "GitHub release cache must be between 5 and 1440 minutes.")
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
builder.Services.AddHttpClient("atlas-alerts", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<AlertMaintenanceWorker>();
builder.Services.AddHostedService<AlertNotificationWorker>();
builder.Services.AddHttpClient("atlas-server-update", client => client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddHostedService<ServerUpdateWorker>();
builder.Services.AddHttpClient("atlas-github-release", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<GitHubReleaseCatalog>();

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
    "/api/v1/agent-installers/token",
    async (
        HttpContext context,
        IAntiforgery antiforgery,
        AtlasDatabase storage,
        IOptions<RegistrationOptions> options) =>
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid CSRF token." });
        }

        var issued = await storage.CreateRegistrationTokenAsync(
            TimeSpan.FromMinutes(options.Value.TokenLifetimeMinutes));
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(issued);
    })
    .RequireAuthorization(OperatorPolicies.Admin);

app.MapGet(
    "/install/windows.ps1",
    (HttpContext context, IOptions<AgentInstallerOptions> options) =>
    {
        var serverUrl = GetPublicServerUrl(context);
        return Results.Text(
            BuildWindowsInstallerScript(
                serverUrl,
                serverUrl + options.Value.AgentPackagePath),
            "text/plain; charset=utf-8");
    });

app.MapGet(
    "/install/atlas-ca.crt",
    (IConfiguration configuration) =>
    {
        var certificatePath = configuration[
            "Kestrel:Certificates:Default:Path"]
            ?? configuration["ASPNETCORE_Kestrel__Certificates__Default__Path"];
        var certificatePassword = configuration[
            "Kestrel:Certificates:Default:Password"]
            ?? configuration["ASPNETCORE_Kestrel__Certificates__Default__Password"];
        if (string.IsNullOrWhiteSpace(certificatePath)
            || !File.Exists(certificatePath))
        {
            return Results.NotFound();
        }

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        var pem = PemEncoding.WriteString(
            "CERTIFICATE",
            certificate.Export(X509ContentType.Cert));
        return Results.Text(
            pem,
            "application/x-pem-file; charset=utf-8",
            System.Text.Encoding.UTF8);
    });

app.MapGet(
    "/install/linux.sh",
    (HttpContext context, IOptions<AgentInstallerOptions> options) =>
    {
        var serverUrl = GetPublicServerUrl(context);
        return Results.Text(
            BuildLinuxInstallerScript(
                serverUrl,
                serverUrl + options.Value.AgentPackagePath),
            "text/plain; charset=utf-8");
    });
app.MapGet(
    "/api/v1/auth/setup-status",
    async (AtlasDatabase storage, CancellationToken cancellationToken) =>
        Results.Ok(new { required = !await storage.HasOperatorsAsync(cancellationToken) }));

app.MapPost(
    "/api/v1/auth/setup",
    async (
        InitialOperatorSetupRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid CSRF token." });
        }

        if (await storage.HasOperatorsAsync(cancellationToken))
        {
            return Results.Conflict(new { error = "Initial setup has already been completed." });
        }

        try
        {
            var identity = await storage.CreateOperatorAsync(
                request.Username,
                request.Password,
                OperatorRoles.Administrator,
                cancellationToken);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, identity.OperatorId.ToString("D")),
                new Claim(ClaimTypes.Name, identity.Username),
                new Claim(ClaimTypes.Role, identity.Role),
            };
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.Created("/api/v1/operators", new OperatorSessionResponse(identity.Username, identity.Role));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error = "Initial setup has already been completed." });
        }
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
        await storage.EvaluateAlertsAsync(agentId, stored.Metrics, cancellationToken);
        return Results.Created(
            $"/api/v1/agents/{agentId:D}/snapshots/{stored.Id}",
            stored);
    });

app.MapGet(
    "/api/v1/agents/{agentId:guid}/updates/pending",
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
        var deployment = await storage.GetPendingAgentUpdateAsync(agentId, cancellationToken);
        return deployment is null
            ? Results.NoContent()
            : Results.Ok(new AgentUpdateManifest(
                deployment.Id, deployment.Version, deployment.PackageUrl, deployment.Sha256));
    });

app.MapPost(
    "/api/v1/agents/{agentId:guid}/updates/{deploymentId:long}/status",
    async (
        Guid agentId,
        long deploymentId,
        HttpContext context,
        AgentUpdateStatusRequest request,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        if (!await AuthenticateAsync(context, storage, agentId, cancellationToken))
        {
            return Results.Unauthorized();
        }
        try
        {
            return await storage.UpdateAgentUpdateStatusAsync(
                agentId, deploymentId, request.State, request.Detail, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    });

app.MapPost(
    "/api/v1/agents/{agentId:guid}/updates",
    async (
        Guid agentId,
        ClaimsPrincipal user,
        HttpContext context,
        IAntiforgery antiforgery,
        IOptions<AgentUpdateOptions> options,
        GitHubReleaseCatalog releases,
        AtlasDatabase storage,
        CancellationToken cancellationToken) =>
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid CSRF token." });
        }
        var operatorId = GetOperatorId(user);
        if (operatorId is null)
        {
            return Results.Forbid();
        }
        var update = options.Value;
        if (!update.Enabled)
        {
            var published = await releases.GetLatestAsync(cancellationToken);
            if (published is null)
            {
                return Results.Conflict(new { error = "No Agent update release is available." });
            }
            update = new AgentUpdateOptions
            {
                Enabled = true,
                Version = published.Version,
                PackageUrl = published.AgentPackageUrl,
                Sha256 = published.AgentSha256,
            };
        }
        var deployment = await storage.RequestAgentUpdateAsync(
            agentId, operatorId.Value, update, cancellationToken);
        return deployment is null
            ? Results.NotFound()
            : Results.Created($"/api/v1/agents/{agentId:D}/updates/{deployment.Id}", deployment);
    })
    .RequireAuthorization(OperatorPolicies.Admin);

app.MapGet(
    "/api/v1/agents/{agentId:guid}/updates",
    async (Guid agentId, AtlasDatabase storage, CancellationToken cancellationToken) =>
        Results.Ok(await storage.ListAgentUpdatesAsync(agentId, cancellationToken)))
    .RequireAuthorization(OperatorPolicies.View);

app.MapPost(
    "/api/v1/agents/{agentId:guid}/archive",
    async (
        Guid agentId,
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

        var operatorId = GetOperatorId(user);
        if (operatorId is null)
        {
            return Results.Forbid();
        }

        return await storage.ArchiveAgentAsync(
            agentId,
            operatorId.Value,
            cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireAuthorization(OperatorPolicies.Admin);

app.MapPost(
    "/api/v1/agents/{agentId:guid}/restore",
    async (
        Guid agentId,
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

        var operatorId = GetOperatorId(user);
        if (operatorId is null)
        {
            return Results.Forbid();
        }

        return await storage.RestoreAgentAsync(
            agentId,
            operatorId.Value,
            cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireAuthorization(OperatorPolicies.Admin);

app.MapDelete(
    "/api/v1/agents/{agentId:guid}",
    async (
        Guid agentId,
        [FromBody] AgentDeletionRequest? request,
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

        var operatorId = GetOperatorId(user);
        if (operatorId is null)
        {
            return Results.Forbid();
        }

        return await storage.DeleteAgentAsync(
            agentId,
            operatorId.Value,
            request?.DeleteSnapshots ?? false,
            cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/alert-rules", async (AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListAlertRulesAsync(ct))).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/releases/latest", async (GitHubReleaseCatalog releases, CancellationToken ct) =>
{
    var release = await releases.GetLatestAsync(ct);
    return release is null ? Results.NoContent() : Results.Ok(release);
}).RequireAuthorization(OperatorPolicies.View);
app.MapPost("/api/v1/alert-rules", async ([FromBody] AlertRuleRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return Results.Created("/api/v1/alert-rules",await s.CreateAlertRuleAsync(x,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapPut("/api/v1/alert-rules/{id:long}", async(long id,[FromBody] AlertRuleRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return await s.UpdateAlertRuleAsync(id,x,ct)?Results.NoContent():Results.NotFound();}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapDelete("/api/v1/alert-rules/{id:long}", async(long id,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.DeleteAlertRuleAsync(id,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/alerts",async(bool? activeOnly,int? limit,AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListAlertsAsync(activeOnly??true,Math.Clamp(limit??100,1,500),ct))).RequireAuthorization(OperatorPolicies.View);
app.MapPost("/api/v1/alerts/{id:long}/acknowledge",async(long id,ClaimsPrincipal u,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();return await s.AcknowledgeAlertAsync(id,u.Identity?.Name??"operator",ct)?Results.NoContent():Results.NotFound();}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/notification-channels",async(AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListNotificationChannelsAsync(ct))).RequireAuthorization(OperatorPolicies.Admin);
app.MapPost("/api/v1/notification-channels",async([FromBody] NotificationChannelRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return Results.Created("/api/v1/notification-channels",await s.CreateNotificationChannelAsync(x,ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapPut("/api/v1/notification-channels/{id:long}",async(long id,[FromBody] NotificationChannelRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return await s.UpdateNotificationChannelAsync(id,x,ct)?Results.NoContent():Results.NotFound();}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapDelete("/api/v1/notification-channels/{id:long}",async(long id,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.DeleteNotificationChannelAsync(id,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet(
    "/api/v1/agents",
    async (
        bool? includeArchived,
        ClaimsPrincipal user,
        AtlasDatabase storage,
        IOptions<ServerApiOptions> options,
        CancellationToken cancellationToken) =>
    {
        var shouldIncludeArchived = includeArchived ?? false;
        if (shouldIncludeArchived && !user.IsInRole(OperatorRoles.Administrator))
        {
            return Results.Forbid();
        }

        var offlineAfter = TimeSpan.FromSeconds(options.Value.OfflineAfterSeconds);
        return Results.Ok(
            await storage.ListAgentsAsync(
                offlineAfter,
                shouldIncludeArchived,
                cancellationToken));
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
static string GetPublicServerUrl(HttpContext context) =>
    $"{context.Request.Scheme}://{context.Request.Host}";

static string BuildWindowsInstallerScript(string serverUrl, string packageUrl) =>
    """
    $ErrorActionPreference = 'Stop'
    if (-not $env:COREWATCH_ATLAS_REGISTRATION_TOKEN) { throw 'COREWATCH_ATLAS_REGISTRATION_TOKEN is required.' }
    if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this command in an elevated PowerShell session.' }
    $root = Join-Path $env:ProgramFiles 'CoreWatch-Atlas\\Agent'
    $zip = Join-Path $env:TEMP 'corewatch-atlas-agent.zip'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri '__PACKAGE_URL__' -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $root -Force
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { throw '.NET 10 runtime is required. Install it, then rerun this command.' }
    Remove-Item -LiteralPath (Join-Path $root 'data\agent-credentials') -Recurse -Force -ErrorAction SilentlyContinue
    & $dotnet (Join-Path $root 'CoreWatch.Atlas.Agent.dll') --register-agent '__SERVER_URL__'
    $service = 'CoreWatchAtlasAgent'
    if (Get-Service -Name $service -ErrorAction SilentlyContinue) { Stop-Service $service -Force; sc.exe delete $service | Out-Null; Start-Sleep -Seconds 1 }
    $binary = ('"{0}" "{1}"' -f $dotnet, (Join-Path $root 'CoreWatch.Atlas.Agent.dll'))
    New-Service -Name $service -BinaryPathName $binary -DisplayName 'CoreWatch Atlas Agent' -StartupType Automatic | Out-Null
    Start-Service $service
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    Write-Host 'CoreWatch Atlas Agent installation completed.'
    """
    .Replace("__SERVER_URL__", serverUrl, StringComparison.Ordinal)
    .Replace("__PACKAGE_URL__", packageUrl, StringComparison.Ordinal);

static string BuildLinuxInstallerScript(string serverUrl, string packageUrl) =>
    """
    #!/usr/bin/env bash
    set -euo pipefail
    : "${COREWATCH_ATLAS_REGISTRATION_TOKEN:?COREWATCH_ATLAS_REGISTRATION_TOKEN is required}"
    : "${COREWATCH_ATLAS_CA_CERT:?COREWATCH_ATLAS_CA_CERT is required}"
    if [ "$(id -u)" -ne 0 ]; then echo 'Run this command with sudo.' >&2; exit 1; fi
    command -v curl >/dev/null || { echo 'curl is required.' >&2; exit 1; }
    if ! command -v unzip >/dev/null; then
      command -v apt-get >/dev/null || { echo 'unzip is required.' >&2; exit 1; }
      apt-get update
      DEBIAN_FRONTEND=noninteractive apt-get install -y unzip
    fi
    if ! command -v dotnet >/dev/null || ! dotnet --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.'; then
      command -v apt-get >/dev/null || { echo '.NET 10 runtime is required.' >&2; exit 1; }
      apt-get update
      DEBIAN_FRONTEND=noninteractive apt-get install -y ca-certificates curl tar
      install_dir=/usr/local/lib/corewatch-dotnet
      curl --fail --location --silent --show-error https://dot.net/v1/dotnet-install.sh -o /tmp/corewatch-dotnet-install.sh
      bash /tmp/corewatch-dotnet-install.sh --runtime aspnetcore --channel 10.0 --install-dir "$install_dir" --no-path
      ln -sfn "$install_dir/dotnet" /usr/local/bin/dotnet
      rm -f /tmp/corewatch-dotnet-install.sh
    fi
    command -v update-ca-certificates >/dev/null || { echo 'update-ca-certificates is required (Ubuntu/Debian).' >&2; exit 1; }
    test -r "$COREWATCH_ATLAS_CA_CERT" || { echo 'The Atlas CA certificate is unreadable.' >&2; exit 1; }
    root=/opt/corewatch-atlas-agent
    state=/var/lib/corewatch-atlas-agent
    zip=$(mktemp)
    trap 'rm -f "$zip"' EXIT
    install -m 0644 "$COREWATCH_ATLAS_CA_CERT" /usr/local/share/ca-certificates/corewatch-atlas.crt
    update-ca-certificates >/dev/null
    install -d -m 0755 "$root" "$state"
    curl --fail --location --silent --show-error --cacert "$COREWATCH_ATLAS_CA_CERT" '__PACKAGE_URL__' -o "$zip"
    unzip -oq "$zip" -d "$root" || test -f "$root/CoreWatch.Atlas.Agent.dll"
    find "$root/runtimes" -type f -name '*.so' -exec chmod 755 {} \;
    Atlas__CredentialStore__Path="$state" dotnet "$root/CoreWatch.Atlas.Agent.dll" --register-agent '__SERVER_URL__'
    cat >/etc/systemd/system/corewatch-atlas-agent.service <<'UNIT'
    [Unit]
    Description=CoreWatch Atlas Agent
    After=network-online.target
    Wants=network-online.target
    [Service]
    Type=simple
    Environment=Atlas__CredentialStore__Path=/var/lib/corewatch-atlas-agent
    Environment=Atlas__AutomaticUpdate__StatePath=/var/lib/corewatch-atlas-agent/updates
    ExecStart=/usr/bin/env dotnet /opt/corewatch-atlas-agent/CoreWatch.Atlas.Agent.dll
    ExecStopPost=+/bin/sh -c 'test ! -f /var/lib/corewatch-atlas-agent/updates/pending-handoff.json || exec /usr/bin/env dotnet /opt/corewatch-atlas-agent/CoreWatch.Atlas.Agent.dll --apply-agent-update /var/lib/corewatch-atlas-agent/updates/pending-handoff.json'
    Restart=on-failure
    RestartSec=30
    [Install]
    WantedBy=multi-user.target
    UNIT
    systemctl daemon-reload
    systemctl enable --now corewatch-atlas-agent
    echo 'CoreWatch Atlas Agent installation completed.'
    """
    .Replace("__SERVER_URL__", serverUrl, StringComparison.Ordinal)
    .Replace("__PACKAGE_URL__", packageUrl, StringComparison.Ordinal);

static Guid? GetOperatorId(ClaimsPrincipal user) =>
    Guid.TryParse(
        user.FindFirstValue(ClaimTypes.NameIdentifier),
        out var operatorId)
        ? operatorId
        : null;
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

        if (arguments[index] is "--create-operator" or "--role" or "--apply-server-update")
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
