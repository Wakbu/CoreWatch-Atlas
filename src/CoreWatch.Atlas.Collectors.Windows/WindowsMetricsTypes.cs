namespace CoreWatch.Atlas.Collectors.Windows;

internal readonly record struct WindowsCpuSample(
    ulong Total,
    ulong Idle,
    int LogicalProcessorCount);

internal sealed record WindowsMemory(
    ulong TotalBytes,
    ulong AvailableBytes);

internal sealed record WindowsFileSystem(
    string Id,
    string MountPoint,
    ulong TotalBytes,
    ulong AvailableBytes);

internal sealed record WindowsDiskIo(
    string Device,
    ulong ReadBytesTotal,
    ulong WriteBytesTotal);

internal sealed record WindowsNetworkIo(
    string Name,
    ulong ReceiveBytesTotal,
    ulong TransmitBytesTotal);

internal interface IWindowsSystemSource
{
    string HostName { get; }

    string OperatingSystem { get; }

    string Architecture { get; }

    string AgentVersion { get; }

    WindowsCpuSample GetCpuSample();

    WindowsMemory GetMemory();

    TimeSpan GetUptime();

    IReadOnlyList<WindowsFileSystem> GetFileSystems();

    IReadOnlyList<WindowsDiskIo> GetDisks();

    IReadOnlyList<WindowsNetworkIo> GetNetworkInterfaces();
}
// CoreWatch Atlas module: WindowsMetricsTypes.
