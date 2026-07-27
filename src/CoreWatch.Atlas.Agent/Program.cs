using CoreWatch.Atlas.Agent;
using CoreWatch.Atlas.Collectors.Linux;
using CoreWatch.Atlas.Collectors.Windows;

var builder = Host.CreateApplicationBuilder(args);
if (OperatingSystem.IsWindows())
{
    builder.Services.AddAtlasMetricsCollection<WindowsSystemMetricsCollector>(
        builder.Configuration);
}
else if (OperatingSystem.IsLinux())
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
