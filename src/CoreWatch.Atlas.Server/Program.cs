using CoreWatch.Atlas.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOptions<ServerStorageOptions>()
    .Bind(builder.Configuration.GetSection(ServerStorageOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DatabasePath),
        "DatabasePath must not be empty.")
    .ValidateOnStart();
builder.Services.AddSingleton<AtlasDatabase>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
var database = app.Services.GetRequiredService<AtlasDatabase>();
await database.InitializeAsync();

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

app.Run();

public partial class Program;
