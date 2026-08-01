using System.Collections.ObjectModel;

namespace CoreWatch.Atlas.Contracts;

public sealed class AgentIdentity
{
    public AgentIdentity(
        string agentId,
        string hostName,
        string operatingSystem,
        string architecture,
        string agentVersion)
    {
        AgentId = ContractGuard.NotWhiteSpace(agentId);
        HostName = ContractGuard.NotWhiteSpace(hostName);
        OperatingSystem = ContractGuard.NotWhiteSpace(operatingSystem);
        Architecture = ContractGuard.NotWhiteSpace(architecture);
        AgentVersion = ContractGuard.NotWhiteSpace(agentVersion);
    }

    public string AgentId { get; }

    public string HostName { get; }

    public string OperatingSystem { get; }

    public string Architecture { get; }

    public string AgentVersion { get; }
}

public sealed class CpuMetrics
{
    public CpuMetrics(double usageRatio, int logicalProcessorCount)
    {
        if (!double.IsFinite(usageRatio) || usageRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(usageRatio),
                usageRatio,
                "CPU usage ratio must be a finite value from 0 through 1.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(logicalProcessorCount, 1);

        UsageRatio = usageRatio;
        LogicalProcessorCount = logicalProcessorCount;
    }

    public double UsageRatio { get; }

    public int LogicalProcessorCount { get; }
}

public sealed class MemoryMetrics
{
    public MemoryMetrics(ulong totalBytes, ulong availableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(totalBytes);

        if (availableBytes > totalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableBytes),
                availableBytes,
                "Available memory cannot exceed total memory.");
        }

        TotalBytes = totalBytes;
        AvailableBytes = availableBytes;
    }

    public ulong TotalBytes { get; }

    public ulong AvailableBytes { get; }

    public ulong UsedBytes => TotalBytes - AvailableBytes;
}

public sealed class FileSystemMetrics
{
    public FileSystemMetrics(
        string id,
        string mountPoint,
        ulong totalBytes,
        ulong availableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(totalBytes);

        if (availableBytes > totalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableBytes),
                availableBytes,
                "Available space cannot exceed total space.");
        }

        Id = ContractGuard.NotWhiteSpace(id);
        MountPoint = ContractGuard.NotWhiteSpace(mountPoint);
        TotalBytes = totalBytes;
        AvailableBytes = availableBytes;
    }

    public string Id { get; }

    public string MountPoint { get; }

    public ulong TotalBytes { get; }

    public ulong AvailableBytes { get; }

    public ulong UsedBytes => TotalBytes - AvailableBytes;
}

public sealed class DiskIoMetrics
{
    public DiskIoMetrics(
        string device,
        ulong readBytesTotal,
        ulong writeBytesTotal)
    {
        Device = ContractGuard.NotWhiteSpace(device);
        ReadBytesTotal = readBytesTotal;
        WriteBytesTotal = writeBytesTotal;
    }

    public string Device { get; }

    public ulong ReadBytesTotal { get; }

    public ulong WriteBytesTotal { get; }
}

public sealed class NetworkInterfaceMetrics
{
    public NetworkInterfaceMetrics(
        string name,
        ulong receiveBytesTotal,
        ulong transmitBytesTotal)
    {
        Name = ContractGuard.NotWhiteSpace(name);
        ReceiveBytesTotal = receiveBytesTotal;
        TransmitBytesTotal = transmitBytesTotal;
    }

    public string Name { get; }

    public ulong ReceiveBytesTotal { get; }

    public ulong TransmitBytesTotal { get; }
}

public sealed class SystemMetricsSnapshot
{
    public SystemMetricsSnapshot(
        DateTimeOffset capturedAtUtc,
        TimeSpan uptime,
        AgentIdentity agent,
        CpuMetrics cpu,
        MemoryMetrics memory,
        IEnumerable<FileSystemMetrics> fileSystems,
        IEnumerable<DiskIoMetrics> disks,
        IEnumerable<NetworkInterfaceMetrics> networkInterfaces,
        IEnumerable<MonitoredServiceMetrics>? services = null,
        IEnumerable<DiagnosticCheckMetrics>? diagnostics = null)
    {
        if (capturedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Capture timestamp must use the UTC offset.",
                nameof(capturedAtUtc));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(uptime, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(memory);

        CapturedAtUtc = capturedAtUtc;
        Uptime = uptime;
        Agent = agent;
        Cpu = cpu;
        Memory = memory;
        FileSystems = ContractGuard.CopyUnique(
            fileSystems,
            static item => item.Id,
            nameof(fileSystems));
        Disks = ContractGuard.CopyUnique(
            disks,
            static item => item.Device,
            nameof(disks));
        NetworkInterfaces = ContractGuard.CopyUnique(
            networkInterfaces,
            static item => item.Name,
            nameof(networkInterfaces));
        // 서비스 상태는 선택 값이다. 기존 Agent와 이전 Snapshot은 빈 목록으로 처리해
        // 계약 확장 후에도 저장된 이력과 구 버전 Agent를 그대로 읽을 수 있게 한다.
        Services = ContractGuard.CopyUnique(
            services ?? [],
            static item => item.Name,
            nameof(services));
        Diagnostics = ContractGuard.CopyUnique(
            diagnostics ?? [],
            static item => item.Id,
            nameof(diagnostics));
    }

    public DateTimeOffset CapturedAtUtc { get; }

    public TimeSpan Uptime { get; }

    public AgentIdentity Agent { get; }

    public CpuMetrics Cpu { get; }

    public MemoryMetrics Memory { get; }

    public IReadOnlyList<FileSystemMetrics> FileSystems { get; }

    public IReadOnlyList<DiskIoMetrics> Disks { get; }

    public IReadOnlyList<NetworkInterfaceMetrics> NetworkInterfaces { get; }

    public IReadOnlyList<MonitoredServiceMetrics> Services { get; }

    public IReadOnlyList<DiagnosticCheckMetrics> Diagnostics { get; }
}

// 서비스 모니터링은 전체 OS 서비스 목록을 무분별하게 전송하지 않는다. 운영자가 지정한
// 서비스만 수집하도록 하며, 상태 문자열은 Windows와 Linux의 차이를 숨기는 공통 값이다.
public sealed class MonitoredServiceMetrics
{
    public MonitoredServiceMetrics(string name, string status)
    {
        Name = ContractGuard.NotWhiteSpace(name);
        Status = ContractGuard.NotWhiteSpace(status);
    }

    public string Name { get; }
    public string Status { get; }
}

// 프로세스, 컨테이너, URL 등 서로 다른 검사 결과를 동일한 상태/상세 형식으로 전달한다.
// Detail은 비밀 값이나 명령줄을 넣지 않는 짧은 운영 메시지로 제한한다.
public sealed class DiagnosticCheckMetrics
{
    public DiagnosticCheckMetrics(string id, string kind, string status, string? detail = null)
    {
        Id = ContractGuard.NotWhiteSpace(id);
        Kind = ContractGuard.NotWhiteSpace(kind);
        Status = ContractGuard.NotWhiteSpace(status);
        Detail = detail?.Length > 240 ? throw new ArgumentOutOfRangeException(nameof(detail)) : detail;
    }
    public string Id { get; }
    public string Kind { get; }
    public string Status { get; }
    public string? Detail { get; }
}

internal static class ContractGuard
{
    public static string NotWhiteSpace(
        string value,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))]
        string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    public static ReadOnlyCollection<T> CopyUnique<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);

        var items = values.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "Metric collections cannot contain null values.",
                    parameterName);
            }

            if (!keys.Add(keySelector(item)))
            {
                throw new ArgumentException(
                    "Metric collection keys must be unique.",
                    parameterName);
            }
        }

        return Array.AsReadOnly(items);
    }
}
// CoreWatch Atlas module: MetricsContracts.
