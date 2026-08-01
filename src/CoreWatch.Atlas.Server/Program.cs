using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
builder.Services.AddOptions<SmtpReportOptions>().Bind(builder.Configuration.GetSection(SmtpReportOptions.SectionName));
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
builder.Services.AddHostedService<SmtpReportWorker>();
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
app.MapPost("/api/v1/operators",async([FromBody] OperatorCreateRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return Results.Created("/api/v1/operators",await s.CreateOperatorAsync(x.Username,x.Password,x.Role,ct));}catch(Exception e) when(e is ArgumentException or InvalidOperationException){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapPut("/api/v1/operators/{id:guid}",async(Guid id,[FromBody] OperatorUpdateRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.UpdateOperatorAsync(id,x,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/audit/operators",async(int? limit,AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListOperatorAuditAsync(Math.Clamp(limit??200,1,1000),ct))).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/audit/api",async(int? limit,AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListApiAuditAsync(Math.Clamp(limit??200,1,1000),ct))).RequireAuthorization(OperatorPolicies.Admin);

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

app.MapGet("/api/v1/agents/{agentId:guid}/commands/pending",async(Guid agentId,HttpContext context,AtlasDatabase storage,CancellationToken ct)=>!await AuthenticateAsync(context,storage,agentId,ct)?Results.Unauthorized():(await storage.GetPendingAgentCommandAsync(agentId,ct)) is { } command?Results.Ok(command):Results.NoContent());
app.MapPost("/api/v1/agents/{agentId:guid}/commands/{id:long}/status",async(Guid agentId,long id,[FromBody] AgentCommandStatusRequest x,HttpContext context,AtlasDatabase storage,CancellationToken ct)=>!await AuthenticateAsync(context,storage,agentId,ct)?Results.Unauthorized():await storage.UpdateAgentCommandAsync(agentId,id,x,ct)?Results.NoContent():Results.NotFound());
app.MapGet("/api/v1/agents/{agentId:guid}/diagnostics/config",async(Guid agentId,HttpContext context,AtlasDatabase storage,CancellationToken ct)=>!await AuthenticateAsync(context,storage,agentId,ct)?Results.Unauthorized():Results.Ok(await storage.GetAgentDiagnosticsConfigurationAsync(agentId,ct)));

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
app.MapGet("/api/v1/server-groups", async (AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListServerGroupsAsync(ct))).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/agents/{agentId:guid}/asset",async(Guid agentId,AtlasDatabase s,CancellationToken ct)=>{var x=await s.GetAssetMetadataAsync(agentId,ct);return x is null?Results.Ok(new AssetMetadata(agentId,null,null,null,null,[])):Results.Ok(x);}).RequireAuthorization(OperatorPolicies.View);
app.MapPut("/api/v1/agents/{agentId:guid}/asset",async(Guid agentId,[FromBody] AssetMetadataRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();await s.SetAssetMetadataAsync(agentId,x,ct);return Results.NoContent();}).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/asset-tags",async(AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListAssetTagsAsync(ct))).RequireAuthorization(OperatorPolicies.View);
app.MapDelete("/api/v1/asset-tags/{id:long}",async(long id,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.DeleteAssetTagAsync(id,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/maintenance-windows",async(AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListMaintenanceWindowsAsync(ct))).RequireAuthorization(OperatorPolicies.View);
app.MapPost("/api/v1/maintenance-windows",async([FromBody] MaintenanceWindowRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():Results.Created("/api/v1/maintenance-windows",await s.CreateMaintenanceWindowAsync(x,ct))).RequireAuthorization(OperatorPolicies.Admin);
app.MapDelete("/api/v1/maintenance-windows/{id:long}",async(long id,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.DeleteMaintenanceWindowAsync(id,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/agents/{agentId:guid}/report", async (Guid agentId,int? days,AtlasDatabase s,IOptions<ServerApiOptions> o,CancellationToken ct) =>
{
    // 대용량 응답과 장기 보존 정책을 고려해 한 번의 보고서는 최대 90일로 제한한다.
    var to = DateTimeOffset.UtcNow;
    var from = to.AddDays(-Math.Clamp(days ?? 7, 1, 90));
    var agent = await s.GetAgentAsync(agentId, TimeSpan.FromSeconds(o.Value.OfflineAfterSeconds), ct);
    if (agent is null) return Results.NotFound();
    var snapshots = await s.GetSnapshotsAsync(agentId, from, to, 100000, ct);
    var alerts = (await s.ListAlertsAsync(false, 500, ct)).Where(x => x.AgentId == agentId && x.OpenedAtUtc <= to && (x.ResolvedAtUtc is null || x.ResolvedAtUtc >= from)).ToArray();
    return Results.Ok(BuildServerReport(agentId, agent.HostName, from, to, snapshots, alerts));
}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/agents/{agentId:guid}/capacity-forecast", async (Guid agentId,AtlasDatabase s,IOptions<ServerApiOptions> o,CancellationToken ct) =>
{
    var agent = await s.GetAgentAsync(agentId, TimeSpan.FromSeconds(o.Value.OfflineAfterSeconds), ct);
    if (agent is null) return Results.NotFound();
    var snapshots = await s.GetSnapshotsAsync(agentId, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, 100000, ct);
    return Results.Ok(BuildCapacityForecast(agentId, agent.HostName, snapshots));
}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/agents/{agentId:guid}/capacity-forecast/partitions",async(Guid agentId,AtlasDatabase s,IOptions<ServerApiOptions> o,CancellationToken ct)=>{var agent=await s.GetAgentAsync(agentId,TimeSpan.FromSeconds(o.Value.OfflineAfterSeconds),ct);if(agent is null)return Results.NotFound();var snapshots=await s.GetSnapshotsAsync(agentId,DateTimeOffset.UtcNow.AddDays(-30),DateTimeOffset.UtcNow,100000,ct);return Results.Ok(BuildPartitionForecasts(snapshots));}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/agents/{agentId:guid}/report/export",async(Guid agentId,int? days,string? format,AtlasDatabase s,IOptions<ServerApiOptions> o,CancellationToken ct)=>{var to=DateTimeOffset.UtcNow;var from=to.AddDays(-Math.Clamp(days??7,1,90));var agent=await s.GetAgentAsync(agentId,TimeSpan.FromSeconds(o.Value.OfflineAfterSeconds),ct);if(agent is null)return Results.NotFound();var snapshots=await s.GetSnapshotsAsync(agentId,from,to,100000,ct);var alerts=(await s.ListAlertsAsync(false,500,ct)).Where(x=>x.AgentId==agentId&&x.OpenedAtUtc<=to&&(x.ResolvedAtUtc is null||x.ResolvedAtUtc>=from)).ToArray();var report=BuildServerReport(agentId,agent.HostName,from,to,snapshots,alerts);return string.Equals(format,"pdf",StringComparison.OrdinalIgnoreCase)?Results.File(ReportExports.Pdf(report),"application/pdf",$"{agent.HostName}-report.pdf"):Results.File(ReportExports.Csv(report),"text/csv; charset=utf-8",$"{agent.HostName}-report.csv");}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/server-groups/{id:long}/report",async(long id,int? days,AtlasDatabase s,IOptions<ServerApiOptions> o,CancellationToken ct)=>{var to=DateTimeOffset.UtcNow;var from=to.AddDays(-Math.Clamp(days??7,1,90));var reports=new List<ServerReport>();foreach(var agentId in await s.ListGroupAgentIdsAsync(id,ct)){var agent=await s.GetAgentAsync(agentId,TimeSpan.FromSeconds(o.Value.OfflineAfterSeconds),ct);if(agent is null)continue;var snapshots=await s.GetSnapshotsAsync(agentId,from,to,100000,ct);var alerts=(await s.ListAlertsAsync(false,500,ct)).Where(x=>x.AgentId==agentId&&x.OpenedAtUtc<=to&&(x.ResolvedAtUtc is null||x.ResolvedAtUtc>=from)).ToArray();reports.Add(BuildServerReport(agentId,agent.HostName,from,to,snapshots,alerts));}return Results.Ok(reports);}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/server-groups/{id:long}/dashboard",async(long id,AtlasDatabase s,IOptions<ServerApiOptions> o,CancellationToken ct)=>{var ids=await s.ListGroupAgentIdsAsync(id,ct);var agents=new List<AgentSummary>();foreach(var agentId in ids)if(await s.GetAgentAsync(agentId,TimeSpan.FromSeconds(o.Value.OfflineAfterSeconds),ct) is { } agent)agents.Add(agent);var active=(await s.ListAlertsAsync(true,500,ct)).Where(x=>ids.Contains(x.AgentId)).ToArray();return Results.Ok(new{serverCount=agents.Count,onlineCount=agents.Count(x=>x.Online),activeAlertCount=active.Length,servers=agents,alerts=active});}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/assets",async(AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListAssetInventoryAsync(ct))).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/api-tokens",async(AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListApiTokensAsync(ct))).RequireAuthorization(OperatorPolicies.Admin);
app.MapPost("/api/v1/api-tokens",async([FromBody] ApiTokenRequest x,ClaimsPrincipal u,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return Results.Created("/api/v1/api-tokens",await s.CreateApiTokenAsync(x,u.Identity?.Name??"operator",ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapDelete("/api/v1/api-tokens/{id:long}",async(long id,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.RevokeApiTokenAsync(id,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/public/v1/assets",async(HttpContext c,AtlasDatabase s,CancellationToken ct)=>{var auth=c.Request.Headers.Authorization.ToString();var value=auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?auth[7..]:null;return await s.AuthenticateApiTokenAsync(value,"read","GET",c.Request.Path,c.Connection.RemoteIpAddress?.ToString(),ct) is null?Results.Unauthorized():Results.Ok(await s.ListAssetInventoryAsync(ct));});
app.MapGet("/api/public/v1/alerts",async(HttpContext c,AtlasDatabase s,CancellationToken ct)=>{var auth=c.Request.Headers.Authorization.ToString();var value=auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?auth[7..]:null;return await s.AuthenticateApiTokenAsync(value,"alerts","GET",c.Request.Path,c.Connection.RemoteIpAddress?.ToString(),ct) is null?Results.Unauthorized():Results.Ok(await s.ListAlertsAsync(false,500,ct));});
app.MapGet("/api/v1/agents/{agentId:guid}/commands",async(Guid agentId,AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListAgentCommandsAsync(agentId,ct))).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/api/v1/agents/{agentId:guid}/diagnostics",async(Guid agentId,AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.GetAgentDiagnosticsConfigurationAsync(agentId,ct))).RequireAuthorization(OperatorPolicies.View);
app.MapPut("/api/v1/agents/{agentId:guid}/diagnostics",async(Guid agentId,[FromBody] AgentDiagnosticsConfiguration x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{await s.SetAgentDiagnosticsConfigurationAsync(agentId,x,ct);return Results.NoContent();}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapPost("/api/v1/agents/{agentId:guid}/commands",async(Guid agentId,[FromBody] AgentCommandRequest x,ClaimsPrincipal u,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>{if(!await ValidateAntiforgeryAsync(c,a))return Results.BadRequest();try{return Results.Created($"/api/v1/agents/{agentId}/commands",await s.RequestAgentCommandAsync(agentId,x,u.Identity?.Name??"operator",ct));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}}).RequireAuthorization(OperatorPolicies.Admin);
app.MapGet("/api/v1/alerts/{id:long}/incident-summary",async(long id,AtlasDatabase s,CancellationToken ct)=>{var alert=(await s.ListAlertsAsync(false,500,ct)).FirstOrDefault(x=>x.Id==id);if(alert is null)return Results.NotFound();var causes=alert.MetricType switch{"cpu"=>new[]{"High workload","Runaway process","Insufficient CPU capacity"},"memory"=>new[]{"Memory leak","Cache growth","Insufficient memory capacity"},"disk"=>new[]{"Log growth","Backup retention","Temporary file accumulation"},_=>new[]{"Network interruption","Agent service stopped","Host shutdown"}};return Results.Ok(new IncidentSummary(id,$"{alert.HostName}에서 {alert.RuleName} 경고가 {alert.OpenedAtUtc:u}에 발생했습니다.",causes,await s.ListAlertActionsAsync(id,ct)));}).RequireAuthorization(OperatorPolicies.View);
app.MapGet("/metrics/server",async(HttpContext c,ClaimsPrincipal u,AtlasDatabase s,CancellationToken ct)=>{if(u.Identity?.IsAuthenticated!=true){var auth=c.Request.Headers.Authorization.ToString();var value=auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?auth[7..]:null;if(await s.AuthenticateApiTokenAsync(value,"read","GET",c.Request.Path,c.Connection.RemoteIpAddress?.ToString(),ct) is null)return Results.Unauthorized();}var alerts=await s.ListAlertsAsync(true,500,ct);var body="# HELP corewatch_atlas_active_alerts Active alerts by severity.\n# TYPE corewatch_atlas_active_alerts gauge\n"+string.Join("\n",alerts.GroupBy(x=>x.Severity).Select(g=>$"corewatch_atlas_active_alerts{{severity=\"{g.Key}\"}} {g.Count()}"))+"\n";return Results.Text(body,"text/plain; version=0.0.4");});
app.MapPost("/api/v1/server-groups", async ([FromBody] ServerGroupRequest x,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():Results.Created("/api/v1/server-groups",await s.CreateServerGroupAsync(x,ct))).RequireAuthorization(OperatorPolicies.Admin);
app.MapDelete("/api/v1/server-groups/{id:long}", async(long id,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.DeleteServerGroupAsync(id,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
app.MapPut("/api/v1/server-groups/{id:long}/agents/{agentId:guid}", async(long id,Guid agentId,bool member,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.SetAgentGroupAsync(agentId,id,member,ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.Admin);
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
app.MapGet("/api/v1/alerts/{id:long}/timeline",async(long id,AtlasDatabase s,CancellationToken ct)=>Results.Ok(await s.ListAlertActionsAsync(id,ct))).RequireAuthorization(OperatorPolicies.View);
app.MapPost("/api/v1/alerts/{id:long}/actions",async(long id,[FromBody] AlertActionRequest x,ClaimsPrincipal u,HttpContext c,IAntiforgery a,AtlasDatabase s,CancellationToken ct)=>!await ValidateAntiforgeryAsync(c,a)?Results.BadRequest():await s.AddAlertActionAsync(id,x,u.Identity?.Name??"operator",ct)?Results.NoContent():Results.NotFound()).RequireAuthorization(OperatorPolicies.View);
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
    $service = 'CoreWatchAtlasAgent'
    # 기존 서비스가 DLL을 붙잡은 상태에서 압축을 풀면 Access denied가 발생한다.
    # 서비스 삭제 전에 PID를 보관하고, 프로세스가 실제로 끝날 때까지 기다린 뒤 설치 폴더를 교체한다.
    $existing = Get-CimInstance Win32_Service -Filter "Name='$service'" -ErrorAction SilentlyContinue
    if ($existing) {
      Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
      if ($existing.ProcessId -gt 0) { Wait-Process -Id $existing.ProcessId -Timeout 20 -ErrorAction SilentlyContinue }
      sc.exe delete $service | Out-Null
      Start-Sleep -Seconds 1
    }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri '__PACKAGE_URL__' -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $root -Force
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { throw '.NET 10 runtime is required. Install it, then rerun this command.' }
    Remove-Item -LiteralPath (Join-Path $root 'data\agent-credentials') -Recurse -Force -ErrorAction SilentlyContinue
    & $dotnet (Join-Path $root 'CoreWatch.Atlas.Agent.dll') --register-agent '__SERVER_URL__'
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

static ServerReport BuildServerReport(Guid agentId, string hostName, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<SnapshotRecord> snapshots, IReadOnlyList<AlertRecord> alerts)
{
    static double? Value(JsonElement x, string name)
    {
        try
        {
            return name switch
            {
                "cpu" => x.GetProperty("cpu").GetProperty("usageRatio").GetDouble() * 100,
                "memory" => 100 * (1 - x.GetProperty("memory").GetProperty("availableBytes").GetDouble() / x.GetProperty("memory").GetProperty("totalBytes").GetDouble()),
                "disk" => x.GetProperty("fileSystems").EnumerateArray().Select(f => (total:f.GetProperty("totalBytes").GetDouble(),free:f.GetProperty("availableBytes").GetDouble())).Aggregate((0d,0d),(a,b)=>(a.Item1+b.total,a.Item2+b.free)) is var d && d.Item1 > 0 ? 100 * (1 - d.Item2 / d.Item1) : null,
                _ => null,
            };
        }
        catch (KeyNotFoundException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
    static MetricReport Metric(IReadOnlyList<SnapshotRecord> x, string type)
    {
        var values = x.Select(s => Value(s.Metrics, type)).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return new MetricReport(values.Length == 0 ? null : values.Average(), values.Length == 0 ? null : values.Max(), values.Length == 0 ? null : values[0]);
    }
    // 기본 수집 주기(15초)를 기준으로 수집 성공 비율을 가동률로 표시한다.
    // Agent가 다른 주기로 운영되더라도 100%를 넘지 않도록 상한을 둔다.
    var expected = Math.Max(1, (to - from).TotalSeconds / 15);
    return new ServerReport(agentId, hostName, from, to, snapshots.Count, Math.Min(100, snapshots.Count / expected * 100), Metric(snapshots, "cpu"), Metric(snapshots, "memory"), Metric(snapshots, "disk"), alerts);
}

static CapacityForecast BuildCapacityForecast(Guid agentId, string hostName, IReadOnlyList<SnapshotRecord> snapshots)
{
    // 파일 시스템을 합산해 서버 전체의 사용률로 환산한다. 마운트별 예측은 별도 정책이 필요하므로
    // 여기서는 운영자가 빠르게 증설 대상을 찾을 수 있는 단일 지표만 제공한다.
    static double? Disk(JsonElement x)
    {
        try { var v=x.GetProperty("fileSystems").EnumerateArray().Select(f=>(f.GetProperty("totalBytes").GetDouble(),f.GetProperty("availableBytes").GetDouble())).Aggregate((0d,0d),(a,b)=>(a.Item1+b.Item1,a.Item2+b.Item2)); return v.Item1>0?100*(1-v.Item2/v.Item1):null; }
        catch (InvalidOperationException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }
    // 첫 관측치와 마지막 관측치의 차이를 일 단위 증가율로 사용한다. 일시적인 감소나
    // 관측 부족 상태에서는 임의의 부족 날짜를 만들지 않고 null을 반환한다.
    var points=snapshots.OrderBy(x=>x.CapturedAtUtc).Select(x=>(x.CapturedAtUtc,Disk(x.Metrics))).Where(x=>x.Item2.HasValue).ToArray();
    if(points.Length<2)return new(agentId,hostName,points.LastOrDefault().Item2,null,null);
    var first=points[0];var last=points[^1];var days=(last.CapturedAtUtc-first.CapturedAtUtc).TotalDays;var growth=days>0?(last.Item2!.Value-first.Item2!.Value)/days:0;
    return new(agentId,hostName,last.Item2,growth>0?growth:null,growth>0?(100-last.Item2!.Value)/growth:null);
}

static IReadOnlyList<PartitionCapacityForecast> BuildPartitionForecasts(IReadOnlyList<SnapshotRecord> snapshots)
{
    var points=new Dictionary<string,List<(DateTimeOffset At,string Mount,double Used)>>();
    foreach(var snapshot in snapshots.OrderBy(x=>x.CapturedAtUtc))try{foreach(var f in snapshot.Metrics.GetProperty("fileSystems").EnumerateArray()){var total=f.GetProperty("totalBytes").GetDouble();if(total<=0)continue;var id=f.GetProperty("id").GetString()??"unknown";var mount=f.GetProperty("mountPoint").GetString()??id;var used=100*(1-f.GetProperty("availableBytes").GetDouble()/total);if(!points.TryGetValue(id,out var list))points[id]=list=[];list.Add((snapshot.CapturedAtUtc,mount,used));}}catch(KeyNotFoundException){}catch(InvalidOperationException){}
    return points.Select(x=>{var first=x.Value[0];var last=x.Value[^1];var days=(last.At-first.At).TotalDays;var growth=x.Value.Count>1&&days>0?(last.Used-first.Used)/days:0;return new PartitionCapacityForecast(x.Key,last.Mount,last.Used,growth>0?growth:null,growth>0?(100-last.Used)/growth:null);}).OrderBy(x=>x.DaysUntilFull??double.MaxValue).ToArray();
}

public partial class Program;
// CoreWatch Atlas module: Program.
