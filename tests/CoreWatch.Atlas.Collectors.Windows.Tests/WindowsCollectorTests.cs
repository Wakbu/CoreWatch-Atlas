using CoreWatch.Atlas.Collectors.Windows;

namespace CoreWatch.Atlas.Collectors.Windows.Tests;

[TestClass]
public sealed class WindowsSystemMetricsCollectorTests
{
    [TestMethod]
    public void CpuCountersCalculateUsageFromTwoSamples()
    {
        var metrics = WindowsSystemMetricsCollector.CalculateCpu(
            new WindowsCpuSample(1_000, 700, 4),
            new WindowsCpuSample(1_100, 760, 4));

        Assert.AreEqual(0.4, metrics.UsageRatio, 0.000001);
        Assert.AreEqual(4, metrics.LogicalProcessorCount);
    }

    [TestMethod]
    public void RegressingCpuCountersFailInsteadOfReturningZero()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            WindowsSystemMetricsCollector.CalculateCpu(
                new WindowsCpuSample(1_000, 700, 4),
                new WindowsCpuSample(900, 600, 4)));
    }

    [TestMethod]
    public async Task FixtureSourceBuildsCompleteSnapshot()
    {
        var collector = new WindowsSystemMetricsCollector(
            new FixtureWindowsSystemSource(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(1));

        var snapshot = await collector.CaptureAsync();

        Assert.AreEqual("windows", collector.Platform);
        Assert.AreEqual("windows:test-host", snapshot.Agent.AgentId);
        Assert.AreEqual(0.4, snapshot.Cpu.UsageRatio, 0.000001);
        Assert.AreEqual(16_000UL, snapshot.Memory.TotalBytes);
        Assert.AreEqual(TimeSpan.FromHours(2), snapshot.Uptime);
        Assert.HasCount(1, snapshot.FileSystems);
        Assert.HasCount(1, snapshot.Disks);
        Assert.HasCount(1, snapshot.NetworkInterfaces);
        Assert.AreEqual(TimeSpan.Zero, snapshot.CapturedAtUtc.Offset);
    }

    [TestMethod]
    public async Task CancellationInterruptsCpuSampling()
    {
        var collector = new WindowsSystemMetricsCollector(
            new FixtureWindowsSystemSource(),
            TimeProvider.System,
            TimeSpan.FromMinutes(1));
        using var cancellation =
            new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await collector.CaptureAsync(cancellation.Token));
    }

    [TestMethod]
    public void PublicConstructorRejectsOtherOperatingSystems()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.ThrowsExactly<PlatformNotSupportedException>(
            static () => new WindowsSystemMetricsCollector());
    }

    [TestMethod]
    public async Task LiveWindowsCaptureSatisfiesSnapshotContract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var snapshot = await new WindowsSystemMetricsCollector().CaptureAsync();

        Assert.IsTrue(snapshot.Memory.TotalBytes > 0);
        Assert.IsTrue(snapshot.Cpu.LogicalProcessorCount > 0);
        Assert.IsTrue(snapshot.Uptime > TimeSpan.Zero);
        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.Agent.AgentId));
        Assert.IsNotEmpty(snapshot.FileSystems);
        Assert.IsNotEmpty(snapshot.NetworkInterfaces);
    }
}

internal sealed class FixtureWindowsSystemSource : IWindowsSystemSource
{
    private int cpuSamples;

    public string HostName => "test-host";

    public string OperatingSystem => "Test Windows";

    public string Architecture => "X64";

    public string AgentVersion => "0.1.0-test";

    public WindowsCpuSample GetCpuSample() =>
        Interlocked.Increment(ref cpuSamples) == 1
            ? new WindowsCpuSample(1_000, 700, 4)
            : new WindowsCpuSample(1_100, 760, 4);

    public WindowsMemory GetMemory() => new(16_000, 6_000);

    public TimeSpan GetUptime() => TimeSpan.FromHours(2);

    public IReadOnlyList<WindowsFileSystem> GetFileSystems() =>
        [new WindowsFileSystem("C:\\", "C:\\", 100_000, 40_000)];

    public IReadOnlyList<WindowsDiskIo> GetDisks() =>
        [new WindowsDiskIo("PhysicalDrive0", 1_000, 2_000)];

    public IReadOnlyList<WindowsNetworkIo> GetNetworkInterfaces() =>
        [new WindowsNetworkIo("interface-id", 3_000, 4_000)];
}
