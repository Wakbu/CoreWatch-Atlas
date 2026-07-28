using CoreWatch.Atlas.Server;

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
builder.Services.AddSingleton<AtlasDatabase>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
var database = app.Services.GetRequiredService<AtlasDatabase>();
await database.InitializeAsync();

if (createRegistrationToken)
{
    var options = app.Services
        .GetRequiredService<
            Microsoft.Extensions.Options.IOptions<RegistrationOptions>>()
        .Value;
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

app.Run();

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
