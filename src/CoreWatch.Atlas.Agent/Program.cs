using CoreWatch.Atlas.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAtlasMetricsCollection<UnconfiguredSystemMetricsCollector>(
    builder.Configuration);

var host = builder.Build();
host.Run();
