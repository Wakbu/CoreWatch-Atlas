using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CoreWatch.Atlas.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class ServerMvpTests
{
    [TestMethod]
    public async Task AuthenticatedSnapshotAppearsInQueriesAndOnlineStateChanges()
    {
        using var fixture = new MvpFixture();
        using var client = fixture.CreateClient();
        var agent = await RegisterAgentAsync(fixture, client);
        StringAssert.StartsWith(agent.Credential, "catlas_agent_");

        var upload = await SendSnapshotAsync(
            client,
            agent.AgentId,
            agent.Credential,
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));
        var agents =
            await client.GetFromJsonAsync<AgentSummary[]>("/api/v1/agents");
        var history = await client.GetFromJsonAsync<SnapshotRecord[]>(
            $"/api/v1/agents/{agent.AgentId:D}/snapshots");

        Assert.AreEqual(HttpStatusCode.Created, upload.StatusCode);
        Assert.HasCount(1, agents!);
        var summary = agents![0];
        Assert.IsTrue(summary.Online);
        Assert.IsNotNull(summary.LatestSnapshot);
        Assert.AreEqual(
            0.42,
            summary.LatestSnapshot.Metrics
                .GetProperty("cpu")
                .GetProperty("usageRatio")
                .GetDouble());
        Assert.HasCount(1, history!);

        fixture.TimeProvider.Advance(TimeSpan.FromSeconds(46));
        var offline = await client.GetFromJsonAsync<AgentSummary>(
            $"/api/v1/agents/{agent.AgentId:D}");
        Assert.IsFalse(offline!.Online);
    }

    [TestMethod]
    public async Task RotationAndRevocationInvalidatePreviousCredentials()
    {
        using var fixture = new MvpFixture();
        using var client = fixture.CreateClient();
        var agent = await RegisterAgentAsync(fixture, client);
        StringAssert.StartsWith(agent.Credential, "catlas_agent_");

        using var rotateRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/agents/{agent.AgentId:D}/credentials/rotate",
            agent.Credential);
        var rotateResponse = await client.SendAsync(rotateRequest);
        var rotated =
            await rotateResponse.Content.ReadFromJsonAsync<AgentCredentialResponse>();
        Assert.AreEqual(HttpStatusCode.OK, rotateResponse.StatusCode);
        Assert.IsNotNull(rotated);

        var oldCredentialResponse = await SendSnapshotAsync(
            client,
            agent.AgentId,
            agent.Credential,
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));
        var newCredentialResponse = await SendSnapshotAsync(
            client,
            agent.AgentId,
            rotated.Credential,
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldCredentialResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, newCredentialResponse.StatusCode);

        using var revokeRequest = AuthorizedRequest(
            HttpMethod.Delete,
            $"/api/v1/agents/{agent.AgentId:D}/credentials",
            rotated.Credential);
        var revokeResponse = await client.SendAsync(revokeRequest);
        var revokedCredentialResponse = await SendSnapshotAsync(
            client,
            agent.AgentId,
            rotated.Credential,
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));
        Assert.AreEqual(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            revokedCredentialResponse.StatusCode);

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var auditCommand = connection.CreateCommand();
        auditCommand.CommandText =
            "SELECT COUNT(*) FROM authentication_audit WHERE agent_id = $agentId;";
        auditCommand.Parameters.AddWithValue("$agentId", agent.AgentId.ToString("D"));
        Assert.AreEqual(4L, (long)(await auditCommand.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task InvalidCredentialIsAuditedAndAgentIdMismatchIsRejected()
    {
        using var fixture = new MvpFixture();
        using var client = fixture.CreateClient();
        var agent = await RegisterAgentAsync(fixture, client);
        StringAssert.StartsWith(agent.Credential, "catlas_agent_");

        var invalid = await SendSnapshotAsync(
            client,
            agent.AgentId,
            "catlas_agent_invalid",
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));
        var mismatched = await SendSnapshotAsync(
            client,
            agent.AgentId,
            agent.Credential,
            CreateSnapshot(Guid.NewGuid(), fixture.TimeProvider.GetUtcNow()));

        Assert.AreEqual(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, mismatched.StatusCode);

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM authentication_audit
                    WHERE event_type = 'authentication_failed'),
                (SELECT COUNT(*) FROM snapshots);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(1L, reader.GetInt64(0));
        Assert.AreEqual(0L, reader.GetInt64(1));
    }

    [TestMethod]
    public async Task RetentionDeletesOnlyExpiredSnapshots()
    {
        using var fixture = new MvpFixture();
        using var client = fixture.CreateClient();
        var agent = await RegisterAgentAsync(fixture, client);
        StringAssert.StartsWith(agent.Credential, "catlas_agent_");
        await SendSnapshotAsync(
            client,
            agent.AgentId,
            agent.Credential,
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));
        fixture.TimeProvider.Advance(TimeSpan.FromDays(31));
        await SendSnapshotAsync(
            client,
            agent.AgentId,
            agent.Credential,
            CreateSnapshot(agent.AgentId, fixture.TimeProvider.GetUtcNow()));

        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        var deleted = await database.DeleteSnapshotsOlderThanAsync(
            fixture.TimeProvider.GetUtcNow().AddDays(-30));
        var history = await database.GetSnapshotsAsync(
            agent.AgentId,
            fixture.TimeProvider.GetUtcNow().AddDays(-32),
            fixture.TimeProvider.GetUtcNow(),
            100);

        Assert.AreEqual(1, deleted);
        Assert.HasCount(1, history);
    }

    private static async Task<RegisteredAgent> RegisterAgentAsync(
        MvpFixture fixture,
        HttpClient client)
    {
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        var token = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(15));
        var response = await client.PostAsJsonAsync(
            "/api/v1/agents/register",
            new AgentRegistrationRequest(
                token.Value,
                "mvp-host",
                "Linux",
                "x64",
                "0.1.0"));
        response.EnsureSuccessStatusCode();
        await database.CreateOperatorAsync(
            "mvp-viewer",
            "Atlas-mvp-test-password!",
            OperatorRoles.Viewer);
        var login = await SecurityTestClient.PostAsJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new OperatorLoginRequest(
                "mvp-viewer",
                "Atlas-mvp-test-password!"));
        login.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegisteredAgent>())!;
    }

    private static async Task<HttpResponseMessage> SendSnapshotAsync(
        HttpClient client,
        Guid agentId,
        string credential,
        SystemMetricsSnapshot snapshot)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            $"/api/v1/agents/{agentId:D}/snapshots",
            credential);
        request.Content = JsonContent.Create(snapshot);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string path,
        string credential)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", credential);
        return request;
    }

    private static SystemMetricsSnapshot CreateSnapshot(
        Guid agentId,
        DateTimeOffset capturedAtUtc) =>
        new(
            capturedAtUtc,
            TimeSpan.FromHours(2),
            new AgentIdentity(
                agentId.ToString("D"),
                "mvp-host",
                "Linux",
                "x64",
                "0.1.0"),
            new CpuMetrics(0.42, 4),
            new MemoryMetrics(8_000, 4_000),
            [new FileSystemMetrics("root", "/", 10_000, 5_000)],
            [new DiskIoMetrics("sda", 100, 200)],
            [new NetworkInterfaceMetrics("eth0", 300, 400)]);

    private sealed class MvpFixture : WebApplicationFactory<Program>
    {
        private readonly string _temporaryDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "CoreWatch-Atlas-Mvp-Tests",
                Guid.NewGuid().ToString("N"));

        public string DatabasePath =>
            Path.Combine(_temporaryDirectory, "atlas-mvp-tests.db");

        public ManualTimeProvider TimeProvider { get; } =
            new(new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            [$"{ServerStorageOptions.SectionName}:DatabasePath"] =
                                DatabasePath,
                        });
                });
            builder.ConfigureServices(
                services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(TimeProvider);
                });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_temporaryDirectory))
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
    }

    public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
