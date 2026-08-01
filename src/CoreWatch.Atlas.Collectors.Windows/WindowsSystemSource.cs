using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoreWatch.Atlas.Collectors.Windows;

internal sealed class WindowsSystemSource : IWindowsSystemSource
{
    private const int MaximumPhysicalDrives = 32;

    public string HostName => Environment.MachineName;

    public string OperatingSystem => RuntimeInformation.OSDescription;

    public string Architecture => RuntimeInformation.OSArchitecture.ToString();

    public string AgentVersion =>
        typeof(WindowsSystemSource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(WindowsSystemSource).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public WindowsCpuSample GetCpuSample()
    {
        if (!WindowsNative.GetSystemTimes(
            out var idle,
            out var kernel,
            out var user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new WindowsCpuSample(
            checked(kernel.Value + user.Value),
            idle.Value,
            Environment.ProcessorCount);
    }

    public WindowsMemory GetMemory()
    {
        var status = new MemoryStatus();
        if (!WindowsNative.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new WindowsMemory(
            status.TotalPhysical,
            status.AvailablePhysical);
    }

    public TimeSpan GetUptime() =>
        TimeSpan.FromMilliseconds(WindowsNative.GetTickCount64());

    public IReadOnlyList<WindowsFileSystem> GetFileSystems()
    {
        var metrics = new List<WindowsFileSystem>();
        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return metrics;
        }
        catch (UnauthorizedAccessException)
        {
            return metrics;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady
                    || drive.DriveType != DriveType.Fixed
                    || drive.TotalSize <= 0)
                {
                    continue;
                }

                metrics.Add(new WindowsFileSystem(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    checked((ulong)drive.TotalSize),
                    checked((ulong)drive.AvailableFreeSpace)));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return metrics;
    }

    public IReadOnlyList<WindowsDiskIo> GetDisks()
    {
        var metrics = new List<WindowsDiskIo>();
        for (var index = 0; index < MaximumPhysicalDrives; index++)
        {
            using var handle = WindowsNative.CreateFile(
                $@"\\.\PhysicalDrive{index}",
                0,
                WindowsNative.FileShareRead | WindowsNative.FileShareWrite,
                0,
                WindowsNative.OpenExisting,
                0,
                0);

            if (handle.IsInvalid
                || !WindowsNative.DeviceIoControl(
                    handle,
                    WindowsNative.IoctlDiskPerformance,
                    0,
                    0,
                    out var performance,
                    Marshal.SizeOf<DiskPerformance>(),
                    out _,
                    0)
                || performance.BytesRead < 0
                || performance.BytesWritten < 0)
            {
                continue;
            }

            metrics.Add(new WindowsDiskIo(
                $"PhysicalDrive{index}",
                checked((ulong)performance.BytesRead),
                checked((ulong)performance.BytesWritten)));
        }

        return metrics;
    }

    public IReadOnlyList<WindowsNetworkIo> GetNetworkInterfaces()
    {
        var metrics = new List<WindowsNetworkIo>();
        NetworkInterface[] interfaces;

        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return metrics;
        }

        foreach (var networkInterface in interfaces)
        {
            try
            {
                var statistics = networkInterface.GetIPStatistics();
                if (statistics.BytesReceived < 0 || statistics.BytesSent < 0)
                {
                    continue;
                }

                metrics.Add(new WindowsNetworkIo(
                    networkInterface.Id,
                    checked((ulong)statistics.BytesReceived),
                    checked((ulong)statistics.BytesSent)));
            }
            catch (NetworkInformationException)
            {
            }
        }

        return metrics;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileTime
{
    public uint Low;
    public uint High;

    public readonly ulong Value => ((ulong)High << 32) | Low;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatus
{
    public MemoryStatus()
    {
        Length = checked((uint)Marshal.SizeOf<MemoryStatus>());
    }

    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhysical;
    public ulong AvailablePhysical;
    public ulong TotalPageFile;
    public ulong AvailablePageFile;
    public ulong TotalVirtual;
    public ulong AvailableVirtual;
    public ulong AvailableExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DiskPerformance
{
    public long BytesRead;
    public long BytesWritten;
    public long ReadTime;
    public long WriteTime;
    public long IdleTime;
    public uint ReadCount;
    public uint WriteCount;
    public uint QueueDepth;
    public uint SplitCount;
    public long QueryTime;
    public uint StorageDeviceNumber;
    private ulong storageManagerNamePart1;
    private ulong storageManagerNamePart2;
}

internal static class WindowsNative
{
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint IoctlDiskPerformance = 0x00070020;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        nint inputBuffer,
        int inputBufferSize,
        out DiskPerformance outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        nint overlapped);
}
// CoreWatch Atlas module: WindowsSystemSource.
