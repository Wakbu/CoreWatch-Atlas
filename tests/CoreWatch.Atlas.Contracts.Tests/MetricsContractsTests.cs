using CoreWatch.Atlas.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreWatch.Atlas.Contracts.Tests;

[TestClass]
public sealed class MetricsContractsTests
{
    [TestMethod]
    public void SnapshotPreservesValidMetrics()
    {
        var snapshot = CreateSnapshot();

        Assert.AreEqual(DateTimeOffset.UnixEpoch, snapshot.CapturedAtUtc);
        Assert.AreEqual(TimeSpan.FromHours(1), snapshot.Uptime);
        Assert.AreEqual("atlas-test", snapshot.Agent.AgentId);
        Assert.AreEqual(0.25, snapshot.Cpu.UsageRatio);
        Assert.AreEqual(750UL, snapshot.Memory.UsedBytes);
        Assert.AreEqual(500UL, snapshot.FileSystems[0].UsedBytes);
        Assert.AreEqual(100UL, snapshot.Disks[0].ReadBytesTotal);
        Assert.AreEqual(300UL, snapshot.NetworkInterfaces[0].ReceiveBytesTotal);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(1.0)]
    public void CpuUsageRatioAllowsInclusiveBoundaries(double usageRatio)
    {
        var metrics = new CpuMetrics(usageRatio, 1);

        Assert.AreEqual(usageRatio, metrics.UsageRatio);
    }

    [TestMethod]
    public void CpuMetricsRejectInvalidValues()
    {
        foreach (var usageRatio in new[]
                 {
                     -0.01,
                     1.01,
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity,
                 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => new CpuMetrics(usageRatio, 1));
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new CpuMetrics(0.5, 0));
    }

    [TestMethod]
    public void CapacityMetricsRejectInvalidValues()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MemoryMetrics(0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new MemoryMetrics(100, 101));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new FileSystemMetrics("root", "/", 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new FileSystemMetrics("root", "/", 100, 101));
    }

    [TestMethod]
    public void MetricIdentityRejectsBlankValues()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new AgentIdentity("", "host", "Linux", "x64", "1.0.0"));
        Assert.ThrowsExactly<ArgumentException>(
            () => new FileSystemMetrics("root", " ", 100, 50));
        Assert.ThrowsExactly<ArgumentException>(
            () => new DiskIoMetrics("", 0, 0));
        Assert.ThrowsExactly<ArgumentException>(
            () => new NetworkInterfaceMetrics("", 0, 0));
    }

    [TestMethod]
    public void SnapshotRejectsNonUtcTimestampAndNegativeUptime()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => CreateSnapshot(
                capturedAtUtc: new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(9))));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CreateSnapshot(uptime: TimeSpan.FromSeconds(-1)));
    }

    [TestMethod]
    public void SnapshotCopiesInputCollections()
    {
        var fileSystems = new List<FileSystemMetrics>
        {
            new("root", "/", 1_000, 500),
        };

        var snapshot = CreateSnapshot(fileSystems: fileSystems);
        fileSystems.Clear();

        Assert.HasCount(1, snapshot.FileSystems);
    }

    [TestMethod]
    public void SnapshotRejectsDuplicateMetricKeys()
    {
        var duplicateDisks = new[]
        {
            new DiskIoMetrics("disk0", 1, 2),
            new DiskIoMetrics("disk0", 3, 4),
        };

        Assert.ThrowsExactly<ArgumentException>(
            () => CreateSnapshot(disks: duplicateDisks));
    }

    [TestMethod]
    public async Task CollectorContractSupportsAsynchronousCaptureAndCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        ISystemMetricsCollector collector = new FakeCollector();

        Assert.AreEqual("test", collector.Platform);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await collector.CaptureAsync(cancellationSource.Token));
    }

    private static SystemMetricsSnapshot CreateSnapshot(
        DateTimeOffset? capturedAtUtc = null,
        TimeSpan? uptime = null,
        IEnumerable<FileSystemMetrics>? fileSystems = null,
        IEnumerable<DiskIoMetrics>? disks = null,
        IEnumerable<NetworkInterfaceMetrics>? networkInterfaces = null)
    {
        return new SystemMetricsSnapshot(
            capturedAtUtc ?? DateTimeOffset.UnixEpoch,
            uptime ?? TimeSpan.FromHours(1),
            new AgentIdentity("atlas-test", "host", "Linux", "x64", "1.0.0"),
            new CpuMetrics(0.25, 4),
            new MemoryMetrics(1_000, 250),
            fileSystems ??
            [
                new FileSystemMetrics("root", "/", 1_000, 500),
            ],
            disks ??
            [
                new DiskIoMetrics("disk0", 100, 200),
            ],
            networkInterfaces ??
            [
                new NetworkInterfaceMetrics("eth0", 300, 400),
            ]);
    }

    private sealed class FakeCollector : ISystemMetricsCollector
    {
        public string Platform => "test";

        public ValueTask<SystemMetricsSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CreateSnapshot());
        }
    }
}