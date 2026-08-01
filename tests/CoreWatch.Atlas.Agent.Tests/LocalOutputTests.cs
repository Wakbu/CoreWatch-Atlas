using System.Text.Json;
using CoreWatch.Atlas.Agent;
using CoreWatch.Atlas.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent.Tests;

[TestClass]
public sealed class LocalOutputTests
{
    [TestMethod]
    public async Task PublisherStoresSnapshotAndWritesOneCamelCaseJsonLine()
    {
        var store = new LatestMetricsSnapshotStore();
        var output = new StringWriter();
        var publisher = new MetricsSnapshotPublisher(
            store,
            output,
            Options.Create(new LocalOutputOptions { JsonEnabled = true }));
        var snapshot = CreateSnapshot();

        await publisher.PublishAsync(snapshot, CancellationToken.None);

        Assert.AreSame(snapshot, store.Latest);
        var line = output.ToString().Trim();
        using var document = JsonDocument.Parse(line);
        Assert.AreEqual("agent-test",
            document.RootElement.GetProperty("agent").GetProperty("agentId").GetString());
        Assert.AreEqual(0.25,
            document.RootElement.GetProperty("cpu").GetProperty("usageRatio").GetDouble());
        Assert.DoesNotContain("\n", line);
    }

    [TestMethod]
    public async Task DisabledJsonStillUpdatesLatestSnapshot()
    {
        var store = new LatestMetricsSnapshotStore();
        var output = new StringWriter();
        var publisher = new MetricsSnapshotPublisher(
            store,
            output,
            Options.Create(new LocalOutputOptions { JsonEnabled = false }));
        var snapshot = CreateSnapshot();

        await publisher.PublishAsync(snapshot, CancellationToken.None);

        Assert.AreSame(snapshot, store.Latest);
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public void PrometheusFormatterUsesCountersExactIntegersAndEscapedLabels()
    {
        var text = PrometheusMetricsFormatter.Format(CreateSnapshot());

        Assert.DoesNotContain("\r", text);
        Assert.Contains(
            "# TYPE corewatch_atlas_disk_read_bytes_total counter",
            text);
        Assert.Contains(
            "corewatch_atlas_disk_read_bytes_total{device=\"disk\\\"0\"} 18446744073709551615",
            text);
        Assert.Contains(
            "corewatch_atlas_network_receive_bytes_total{device=\"eth0\"} 3000",
            text);
        Assert.Contains(
            "corewatch_atlas_agent_info{agent_id=\"agent-test\",host=\"host\\\\name\"",
            text);
    }

    [TestMethod]
    public async Task EndpointReturnsUnavailableBeforeFirstSnapshot()
    {
        var worker = CreateEndpointWorker(new LatestMetricsSnapshotStore());
        var context = CreateHttpContext();

        await worker.WriteMetricsAsync(context);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable,
            context.Response.StatusCode);
        Assert.AreEqual(PrometheusEndpointWorker.ContentType,
            context.Response.ContentType);
        Assert.Contains("No metrics snapshot", ReadResponse(context));
    }

    [TestMethod]
    public async Task EndpointReturnsLatestPrometheusSnapshot()
    {
        var store = new LatestMetricsSnapshotStore();
        store.Update(CreateSnapshot());
        var worker = CreateEndpointWorker(store);
        var context = CreateHttpContext();

        await worker.WriteMetricsAsync(context);

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("corewatch_atlas_cpu_usage_ratio 0.25",
            ReadResponse(context));
    }

    [TestMethod]
    public void EnabledEndpointRejectsUrlWithPath()
    {
        var options = new LocalOutputOptions
        {
            Prometheus = new PrometheusEndpointOptions
            {
                Enabled = true,
                Url = "http://127.0.0.1:9464/private",
            },
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new PrometheusEndpointWorker(
                new LatestMetricsSnapshotStore(),
                NullLogger<PrometheusEndpointWorker>.Instance,
                Options.Create(options)));
    }

    private static PrometheusEndpointWorker CreateEndpointWorker(
        LatestMetricsSnapshotStore store) =>
        new(
            store,
            NullLogger<PrometheusEndpointWorker>.Instance,
            Options.Create(new LocalOutputOptions()));

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static SystemMetricsSnapshot CreateSnapshot() =>
        new(
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromHours(1),
            new AgentIdentity(
                "agent-test",
                "host\\name",
                "Test OS",
                "x64",
                "1.0.0"),
            new CpuMetrics(0.25, 4),
            new MemoryMetrics(1_000, 500),
            [new FileSystemMetrics("root", "/", 10_000, 4_000)],
            [new DiskIoMetrics("disk\"0", ulong.MaxValue, 2_000)],
            [new NetworkInterfaceMetrics("eth0", 3_000, 4_000)]);
}
// CoreWatch Atlas module: LocalOutputTests.
