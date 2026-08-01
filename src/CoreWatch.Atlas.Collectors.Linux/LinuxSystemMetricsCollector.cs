using System.Runtime.InteropServices;
using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Collectors.Linux;

public sealed class LinuxSystemMetricsCollector : ISystemMetricsCollector
{
    private static readonly TimeSpan DefaultCpuSampleInterval = TimeSpan.FromMilliseconds(100);
    private readonly ILinuxSystemSource source;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan cpuSampleInterval;

    public LinuxSystemMetricsCollector()
        : this(new LinuxSystemSource(), TimeProvider.System, DefaultCpuSampleInterval)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "The Linux metrics collector can run only on Linux.");
        }
    }

    internal LinuxSystemMetricsCollector(
        ILinuxSystemSource source,
        TimeProvider timeProvider,
        TimeSpan cpuSampleInterval)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cpuSampleInterval, TimeSpan.Zero);
        this.source = source;
        this.timeProvider = timeProvider;
        this.cpuSampleInterval = cpuSampleInterval;
    }

    public string Platform => "linux";

    public async ValueTask<SystemMetricsSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var firstCpu = LinuxProcParser.ParseCpuSample(
            await source.ReadAllTextAsync("/proc/stat", cancellationToken).ConfigureAwait(false));

        await Task.Delay(cpuSampleInterval, timeProvider, cancellationToken).ConfigureAwait(false);

        var secondCpu = LinuxProcParser.ParseCpuSample(
            await source.ReadAllTextAsync("/proc/stat", cancellationToken).ConfigureAwait(false));
        var memory = LinuxProcParser.ParseMemory(
            await source.ReadAllTextAsync("/proc/meminfo", cancellationToken).ConfigureAwait(false));
        var uptime = LinuxProcParser.ParseUptime(
            await source.ReadAllTextAsync("/proc/uptime", cancellationToken).ConfigureAwait(false));
        var disks = await ReadOptionalAsync(
            "/proc/diskstats", LinuxProcParser.ParseDiskIo, cancellationToken).ConfigureAwait(false);
        var network = await ReadOptionalAsync(
            "/proc/net/dev", LinuxProcParser.ParseNetwork, cancellationToken).ConfigureAwait(false);
        var fileSystems = ReadFileSystems();

        return new SystemMetricsSnapshot(
            timeProvider.GetUtcNow(),
            uptime,
            new AgentIdentity(
                await GetAgentIdAsync(cancellationToken).ConfigureAwait(false),
                source.HostName,
                source.OperatingSystem,
                source.Architecture,
                source.AgentVersion),
            LinuxProcParser.CalculateCpu(firstCpu, secondCpu),
            memory,
            fileSystems,
            disks,
            network);
    }

    private IReadOnlyList<FileSystemMetrics> ReadFileSystems()
    {
        try
        {
            return source.GetFileSystems()
                .Select(static item => new FileSystemMetrics(
                    item.Id, item.MountPoint, item.TotalBytes, item.AvailableBytes))
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async ValueTask<string> GetAgentIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var machineId = await source
                .ReadAllTextAsync("/etc/machine-id", cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(machineId))
            {
                return machineId.Trim();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return $"linux:{source.HostName}";
    }

    private async ValueTask<IReadOnlyList<T>> ReadOptionalAsync<T>(
        string path,
        Func<string, IReadOnlyList<T>> parser,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await source.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return parser(content);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
// CoreWatch Atlas module: LinuxSystemMetricsCollector.
