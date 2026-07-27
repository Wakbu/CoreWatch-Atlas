using System.Runtime.InteropServices;
using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Collectors.Windows;

public sealed class WindowsSystemMetricsCollector : ISystemMetricsCollector
{
    private static readonly TimeSpan DefaultCpuSampleInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly IWindowsSystemSource source;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan cpuSampleInterval;

    public WindowsSystemMetricsCollector()
        : this(new WindowsSystemSource(), TimeProvider.System, DefaultCpuSampleInterval)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "The Windows metrics collector can run only on Windows.");
        }
    }

    internal WindowsSystemMetricsCollector(
        IWindowsSystemSource source,
        TimeProvider timeProvider,
        TimeSpan cpuSampleInterval)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            cpuSampleInterval,
            TimeSpan.Zero);

        this.source = source;
        this.timeProvider = timeProvider;
        this.cpuSampleInterval = cpuSampleInterval;
    }

    public string Platform => "windows";

    public async ValueTask<SystemMetricsSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var firstCpu = source.GetCpuSample();

        await Task.Delay(cpuSampleInterval, timeProvider, cancellationToken)
            .ConfigureAwait(false);

        var secondCpu = source.GetCpuSample();
        cancellationToken.ThrowIfCancellationRequested();
        var memory = source.GetMemory();
        var uptime = source.GetUptime();
        var fileSystems = source.GetFileSystems();
        var disks = source.GetDisks();
        var networkInterfaces = source.GetNetworkInterfaces();

        return new SystemMetricsSnapshot(
            timeProvider.GetUtcNow(),
            uptime,
            new AgentIdentity(
                $"windows:{source.HostName}",
                source.HostName,
                source.OperatingSystem,
                source.Architecture,
                source.AgentVersion),
            CalculateCpu(firstCpu, secondCpu),
            new MemoryMetrics(memory.TotalBytes, memory.AvailableBytes),
            fileSystems.Select(static item => new FileSystemMetrics(
                item.Id,
                item.MountPoint,
                item.TotalBytes,
                item.AvailableBytes)),
            disks.Select(static item => new DiskIoMetrics(
                item.Device,
                item.ReadBytesTotal,
                item.WriteBytesTotal)),
            networkInterfaces.Select(static item => new NetworkInterfaceMetrics(
                item.Name,
                item.ReceiveBytesTotal,
                item.TransmitBytesTotal)));
    }

    internal static CpuMetrics CalculateCpu(
        WindowsCpuSample first,
        WindowsCpuSample second)
    {
        if (second.Total <= first.Total || second.Idle < first.Idle)
        {
            throw new InvalidDataException(
                "Windows CPU counters did not advance monotonically.");
        }

        var totalDelta = second.Total - first.Total;
        var idleDelta = second.Idle - first.Idle;
        if (idleDelta > totalDelta)
        {
            throw new InvalidDataException(
                "Windows CPU idle counter delta exceeds total counter delta.");
        }

        return new CpuMetrics(
            (double)(totalDelta - idleDelta) / totalDelta,
            second.LogicalProcessorCount);
    }
}
