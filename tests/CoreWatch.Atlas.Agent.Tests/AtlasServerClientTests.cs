using System.Net;
using System.Text.Json;
using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent.Tests;

[TestClass]
public sealed class AtlasServerClientTests
{
    private static readonly Guid AgentId =
        Guid.Parse("019c16a0-5f52-7000-8000-000000000001");

    [TestMethod]
    public async Task EnabledClientSendsAuthenticatedSnapshotWithPermanentId()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, enabled: true);
        var snapshot = CreateSnapshot();

        await client.SendAsync(snapshot, CancellationToken.None);

        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(
            $"/api/v1/agents/{AgentId:D}/snapshots",
            handler.RequestPath);
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        Assert.AreEqual("catlas_agent_test-secret", handler.AuthorizationValue);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.AreEqual(
            AgentId.ToString("D"),
            document.RootElement.GetProperty("agent").GetProperty("agentId").GetString());
        Assert.AreEqual("collector-local-id", snapshot.Agent.AgentId);
    }

    [TestMethod]
    public async Task DisabledClientDoesNotSend()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, enabled: false);

        await client.SendAsync(CreateSnapshot(), CancellationToken.None);

        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task ServerFailureIsReportedToPublisher()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, enabled: true);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendAsync(CreateSnapshot(), CancellationToken.None));
    }

    [TestMethod]
    public void NonLoopbackHttpServerIsRejected()
    {
        using var httpClient = new HttpClient(
            new RecordingHandler(HttpStatusCode.Created));

        Assert.Throws<InvalidOperationException>(
            () => new AtlasServerClient(
                httpClient,
                Options.Create(
                    new ServerTransmissionOptions
                    {
                        Enabled = true,
                        BaseUrl = "http://atlas.example.test/",
                        AgentId = AgentId.ToString("D"),
                        Credential = "catlas_agent_test-secret",
                    })));
    }
    [TestMethod]
    public async Task PublisherIsolatesServerFailureAndKeepsLatestSnapshot()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, enabled: true);
        var store = new LatestMetricsSnapshotStore();
        var publisher = new MetricsSnapshotPublisher(
            store,
            TextWriter.Null,
            Options.Create(new LocalOutputOptions()),
            client,
            NullLogger<MetricsSnapshotPublisher>.Instance);
        var snapshot = CreateSnapshot();

        await publisher.PublishAsync(snapshot, CancellationToken.None);

        Assert.AreSame(snapshot, store.Latest);
        Assert.AreEqual(1, handler.RequestCount);
    }

    private static AtlasServerClient CreateClient(
        HttpClient httpClient,
        bool enabled) =>
        new(
            httpClient,
            Options.Create(new ServerTransmissionOptions
            {
                Enabled = enabled,
                BaseUrl = "https://atlas.example.test/",
                AgentId = AgentId.ToString("D"),
                Credential = "catlas_agent_test-secret",
            }));

    private static SystemMetricsSnapshot CreateSnapshot() =>
        new(
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMinutes(5),
            new AgentIdentity(
                "collector-local-id",
                "host",
                "Test OS",
                "x64",
                "1.0.0"),
            new CpuMetrics(0.5, 4),
            new MemoryMetrics(1_000, 500),
            [],
            [],
            []);

    private sealed class RecordingHandler(HttpStatusCode responseStatus)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string? RequestPath { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationValue { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestPath = request.RequestUri?.AbsolutePath;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationValue = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(responseStatus);
        }
    }
}
