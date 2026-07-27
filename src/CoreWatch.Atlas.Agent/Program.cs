using CoreWatch.Atlas.Agent;
using CoreWatch.Atlas.Collectors.Linux;

var builder = Host.CreateApplicationBuilder(args);
if (OperatingSystem.IsLinux())
{
    builder.Services.AddAtlasMetricsCollection<LinuxSystemMetricsCollector>(
        builder.Configuration);
}
else
{
    builder.Services.AddAtlasMetricsCollection<UnconfiguredSystemMetricsCollector>(
        builder.Configuration);
}

var host = builder.Build();
host.Run();
