using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Server;

public sealed record SnapshotUploadRequest(
    DateTimeOffset CapturedAtUtc,
    TimeSpan Uptime,
    SnapshotAgent Agent,
    SnapshotCpu Cpu,
    SnapshotMemory Memory,
    IReadOnlyList<SnapshotFileSystem> FileSystems,
    IReadOnlyList<SnapshotDisk> Disks,
    IReadOnlyList<SnapshotNetworkInterface> NetworkInterfaces,
    IReadOnlyList<MonitoredServiceMetrics>? Services = null,
    IReadOnlyList<DiagnosticCheckMetrics>? Diagnostics = null)
{
    public SystemMetricsSnapshot ToContract() =>
        new(
            CapturedAtUtc,
            Uptime,
            new AgentIdentity(
                Agent.AgentId,
                Agent.HostName,
                Agent.OperatingSystem,
                Agent.Architecture,
                Agent.AgentVersion),
            new CpuMetrics(Cpu.UsageRatio, Cpu.LogicalProcessorCount),
            new MemoryMetrics(Memory.TotalBytes, Memory.AvailableBytes),
            FileSystems.Select(
                item => new FileSystemMetrics(
                    item.Id,
                    item.MountPoint,
                    item.TotalBytes,
                    item.AvailableBytes)),
            Disks.Select(
                item => new DiskIoMetrics(
                    item.Device,
                    item.ReadBytesTotal,
                    item.WriteBytesTotal)),
            NetworkInterfaces.Select(
                item => new NetworkInterfaceMetrics(
                    item.Name,
                    item.ReceiveBytesTotal,
                    item.TransmitBytesTotal)),
            Services,
            Diagnostics);
}

public sealed record SnapshotAgent(
    string AgentId,
    string HostName,
    string OperatingSystem,
    string Architecture,
    string AgentVersion);

public sealed record SnapshotCpu(
    double UsageRatio,
    int LogicalProcessorCount);

public sealed record SnapshotMemory(
    ulong TotalBytes,
    ulong AvailableBytes);

public sealed record SnapshotFileSystem(
    string Id,
    string MountPoint,
    ulong TotalBytes,
    ulong AvailableBytes);

public sealed record SnapshotDisk(
    string Device,
    ulong ReadBytesTotal,
    ulong WriteBytesTotal);

public sealed record SnapshotNetworkInterface(
    string Name,
    ulong ReceiveBytesTotal,
    ulong TransmitBytesTotal);
