using CoreWatch.Atlas.Collectors.Linux;

namespace CoreWatch.Atlas.Collectors.Linux.Tests;

[TestClass]
public sealed class LinuxProcParserTests
{
    [TestMethod]
    public void CpuFixtureCalculatesDeltaUsageAndProcessorCount()
    {
        var first = LinuxProcParser.ParseCpuSample(Fixture("stat-1.txt"));
        var second = LinuxProcParser.ParseCpuSample(Fixture("stat-2.txt"));
        var metrics = LinuxProcParser.CalculateCpu(first, second);

        Assert.AreEqual(0.3, metrics.UsageRatio, 0.000001);
        Assert.AreEqual(2, metrics.LogicalProcessorCount);
    }

    [TestMethod]
    public void MemoryFixtureUsesMemAvailableInBytes()
    {
        var metrics = LinuxProcParser.ParseMemory(Fixture("meminfo.txt"));

        Assert.AreEqual(16UL * 1024 * 1024, metrics.TotalBytes);
        Assert.AreEqual(8UL * 1024 * 1024, metrics.AvailableBytes);
    }

    [TestMethod]
    public void DiskFixtureConvertsKernelSectorsToBytes()
    {
        var metrics = LinuxProcParser.ParseDiskIo(Fixture("diskstats.txt"));

        Assert.AreEqual(2, metrics.Count);
        Assert.AreEqual("sda", metrics[0].Device);
        Assert.AreEqual(20UL * 512, metrics[0].ReadBytesTotal);
        Assert.AreEqual(40UL * 512, metrics[0].WriteBytesTotal);
    }

    [TestMethod]
    public void NetworkFixtureReadsReceiveAndTransmitCounters()
    {
        var metrics = LinuxProcParser.ParseNetwork(Fixture("net-dev.txt"));

        Assert.AreEqual(2, metrics.Count);
        Assert.AreEqual("eth0", metrics[1].Name);
        Assert.AreEqual(3000UL, metrics[1].ReceiveBytesTotal);
        Assert.AreEqual(4000UL, metrics[1].TransmitBytesTotal);
    }

    [TestMethod]
    public void UptimeFixtureUsesInvariantSeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(123.45),
            LinuxProcParser.ParseUptime(Fixture("uptime.txt")));
    }

    [TestMethod]
    public void InvalidRequiredFixtureFailsInsteadOfReturningZero()
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => LinuxProcParser.ParseMemory("MemFree: 1 kB"));
    }

    internal static string Fixture(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

[TestClass]
public sealed class LinuxSystemMetricsCollectorTests
{
    [TestMethod]
    public async Task CaptureBuildsSnapshotAndOmitsUnavailableOptionalSources()
    {
        var source = new FixtureSystemSource(optionalSourcesUnavailable: true);
        var collector = new LinuxSystemMetricsCollector(
            source, TimeProvider.System, TimeSpan.FromMilliseconds(1));

        var snapshot = await collector.CaptureAsync();

        Assert.AreEqual("linux", collector.Platform);
        Assert.AreEqual("linux:test-host", snapshot.Agent.AgentId);
        Assert.AreEqual(0.3, snapshot.Cpu.UsageRatio, 0.000001);
        Assert.AreEqual(TimeSpan.FromSeconds(123.45), snapshot.Uptime);
        Assert.HasCount(1, snapshot.FileSystems);
        Assert.IsEmpty(snapshot.Disks);
        Assert.IsEmpty(snapshot.NetworkInterfaces);
        Assert.AreEqual(TimeSpan.Zero, snapshot.CapturedAtUtc.Offset);
    }

    [TestMethod]
    public async Task MissingRequiredProcFileIsPropagated()
    {
        var collector = new LinuxSystemMetricsCollector(
            new FixtureSystemSource(requiredSourceUnavailable: true),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            async () => await collector.CaptureAsync());
    }

    [TestMethod]
    public async Task CancellationInterruptsCpuSampling()
    {
        var collector = new LinuxSystemMetricsCollector(
            new FixtureSystemSource(),
            TimeProvider.System,
            TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await collector.CaptureAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task LiveProcCaptureWorksOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var snapshot = await new LinuxSystemMetricsCollector().CaptureAsync();

        Assert.IsTrue(snapshot.Memory.TotalBytes > 0);
        Assert.IsTrue(snapshot.Cpu.LogicalProcessorCount > 0);
        Assert.IsTrue(snapshot.Uptime > TimeSpan.Zero);
        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.Agent.AgentId));
    }
}

internal sealed class FixtureSystemSource : ILinuxSystemSource
{
    private readonly bool optionalSourcesUnavailable;
    private readonly bool requiredSourceUnavailable;
    private int statReads;

    public FixtureSystemSource(
        bool optionalSourcesUnavailable = false,
        bool requiredSourceUnavailable = false)
    {
        this.optionalSourcesUnavailable = optionalSourcesUnavailable;
        this.requiredSourceUnavailable = requiredSourceUnavailable;
    }

    public string HostName => "test-host";
    public string OperatingSystem => "Test Linux";
    public string Architecture => "X64";
    public string AgentVersion => "0.1.0-test";

    public ValueTask<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requiredSourceUnavailable && path == "/proc/stat")
        {
            throw new FileNotFoundException("Required fixture is unavailable.", path);
        }

        if (optionalSourcesUnavailable
            && path is "/proc/diskstats" or "/proc/net/dev" or "/etc/machine-id")
        {
            throw new UnauthorizedAccessException("Fixture access denied.");
        }

        var content = path switch
        {
            "/proc/stat" => LinuxProcParserTests.Fixture(
                Interlocked.Increment(ref statReads) == 1 ? "stat-1.txt" : "stat-2.txt"),
            "/proc/meminfo" => LinuxProcParserTests.Fixture("meminfo.txt"),
            "/proc/diskstats" => LinuxProcParserTests.Fixture("diskstats.txt"),
            "/proc/net/dev" => LinuxProcParserTests.Fixture("net-dev.txt"),
            "/proc/uptime" => LinuxProcParserTests.Fixture("uptime.txt"),
            "/etc/machine-id" => "fixture-machine-id\n",
            _ => throw new FileNotFoundException("Unknown fixture path.", path),
        };
        return ValueTask.FromResult(content);
    }

    public IReadOnlyList<LinuxFileSystem> GetFileSystems() =>
        [new LinuxFileSystem("root", "/", 10_000, 4_000)];
}
