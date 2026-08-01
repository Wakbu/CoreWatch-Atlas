using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class ServerApiTests
{
    [TestMethod]
    public async Task LiveEndpointReturnsOk()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("ok", body.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task DashboardAndStaticAssetsAreServed()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();

        var dashboard = await client.GetAsync("/");
        var stylesheet = await client.GetAsync("/css/atlas.css");
        var script = await client.GetAsync("/js/atlas.js");
        var alertScript = await client.GetAsync("/js/alerts.js");

        Assert.AreEqual(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.AreEqual("text/html", dashboard.Content.Headers.ContentType?.MediaType);
        StringAssert.Contains(await dashboard.Content.ReadAsStringAsync(), "CoreWatch-Atlas");
        Assert.AreEqual("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        var stylesheetText = await stylesheet.Content.ReadAsStringAsync();
        StringAssert.Contains(stylesheetText, ".backdrop{display:none}");
        StringAssert.Contains(stylesheetText, "--bg:#f5f6f8");
        StringAssert.Contains(stylesheetText, "@media(max-width:1100px)");
        StringAssert.Contains(stylesheetText, ".lifecycle-actions{order:3");
        Assert.IsTrue(script.Content.Headers.ContentType?.MediaType?.Contains("javascript"));
        StringAssert.Contains(
            await script.Content.ReadAsStringAsync(),
            "POLL_INTERVAL_MS=15_000");
        StringAssert.Contains(
            await script.Content.ReadAsStringAsync(),
            "data-lifecycle");
        StringAssert.Contains(
            await script.Content.ReadAsStringAsync(),
            "data-update-agent");
        var enrollmentScript = await client.GetAsync("/js/enrollment.js");
        StringAssert.Contains(
            await enrollmentScript.Content.ReadAsStringAsync(),
            "atlas-ca.crt");
        StringAssert.Contains(
            await enrollmentScript.Content.ReadAsStringAsync(),
            "--cacert");
        StringAssert.Contains(
            await enrollmentScript.Content.ReadAsStringAsync(),
            "curl.exe --fail");
        StringAssert.Contains(
            await script.Content.ReadAsStringAsync(),
            "chart-scale");
        StringAssert.Contains(await script.Content.ReadAsStringAsync(), "atlas:render");
        Assert.IsTrue(alertScript.Content.Headers.ContentType?.MediaType?.Contains("javascript"));
        StringAssert.Contains(await alertScript.Content.ReadAsStringAsync(), "atlas:render");
    }

    [TestMethod]
    public async Task LinuxInstallerRequiresPinnedCertificateBootstrap()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/install/linux.sh");
        var script = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(script, "COREWATCH_ATLAS_CA_CERT");
        StringAssert.Contains(script, "update-ca-certificates");
        StringAssert.Contains(script, "--cacert");
        StringAssert.Contains(script, "apt-get install -y unzip");
        StringAssert.Contains(script, "dotnet-install.sh");
        StringAssert.Contains(script, "--runtime aspnetcore --channel 10.0");
        StringAssert.Contains(script, "ExecStart=/usr/bin/env dotnet");
        StringAssert.Contains(script, "test -f \"$root/CoreWatch.Atlas.Agent.dll\"");
    }

        [TestMethod]
    public async Task AlertRulesAndAlertsAreAvailableToOperators()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();
        await AuthenticateAsync(fixture, client);
        var rules = await client.GetFromJsonAsync<AlertRule[]>("/api/v1/alert-rules");
        var alerts = await client.GetFromJsonAsync<AlertRecord[]>("/api/v1/alerts");
        Assert.IsNotNull(rules);
        Assert.HasCount(4, rules);
        Assert.IsNotNull(alerts);
    }
[TestMethod]
    public async Task ReadyAndStatusEndpointsReportCurrentSchema()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();
        await AuthenticateAsync(fixture, client);

        var ready = await client.GetFromJsonAsync<JsonElement>("/health/ready");
        var status = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");

        Assert.AreEqual("ready", ready.GetProperty("status").GetString());
        Assert.AreEqual(AtlasDatabase.CurrentSchemaVersion, ready.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("CoreWatch-Atlas.Server", status.GetProperty("service").GetString());
        Assert.AreEqual(
            AtlasDatabase.CurrentSchemaVersion,
            status.GetProperty("storage").GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public async Task StartupCreatesExpectedSchema()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();
        await client.GetAsync("/health/ready");

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();

        var tables = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table';");
        var indexes = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'index';");

        CollectionAssert.IsSubsetOf(
            new[] { "schema_migrations", "agents", "snapshots", "registration_tokens", "authentication_audit", "atlas_operators", "operator_authentication_audit" },
            tables);
        CollectionAssert.Contains(indexes, "ix_snapshots_agent_captured_at");
    }

    [TestMethod]
    public async Task InitializationIsIdempotent()
    {
        using var fixture = new ServerFixture();
        using var firstClient = fixture.CreateClient();
        using var secondClient = fixture.CreateClient();

        await firstClient.GetAsync("/health/ready");
        await secondClient.GetAsync("/health/ready");

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";

        Assert.AreEqual((long)AtlasDatabase.CurrentSchemaVersion, (long)(await command.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task AgentUpdateRequiresPerAgentRequestAndRecordsStatus()
    {
        using var fixture = new ServerFixture();
        using var client = fixture.CreateClient();
        await client.GetAsync("/health/ready");
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        var token = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(5));
        var agent = await database.RegisterAgentAsync(
            new AgentRegistrationRequest(
                token.Value, "update-host", "Test OS", "x64", "1.0.0"));
        var operatorIdentity = await database.CreateOperatorAsync(
            "update-admin", "Atlas-update-test-password!", OperatorRoles.Administrator);

        Assert.IsNotNull(agent);
        Assert.IsNull(await database.GetPendingAgentUpdateAsync(agent.AgentId));
        var deployment = await database.RequestAgentUpdateAsync(
            agent.AgentId,
            operatorIdentity.OperatorId,
            new AgentUpdateOptions
            {
                Enabled = true,
                Version = "2.0.0",
                PackageUrl = "https://atlas.example.test/agent.zip",
                Sha256 = new string('a', 64),
            });

        Assert.IsNotNull(deployment);
        Assert.AreEqual("pending", deployment.State);
        Assert.IsTrue(await database.UpdateAgentUpdateStatusAsync(
            agent.AgentId, deployment.Id, "downloading", null));
        var history = await database.ListAgentUpdatesAsync(agent.AgentId);
        Assert.HasCount(1, history);
        Assert.AreEqual("downloading", history[0].State);
    }

    private static async Task AuthenticateAsync(
        ServerFixture fixture,
        HttpClient client)
    {
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync(
            "server-test-admin",
            "Atlas-server-test-password!",
            OperatorRoles.Administrator);
        var response = await SecurityTestClient.PostAsJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new OperatorLoginRequest(
                "server-test-admin",
                "Atlas-server-test-password!"));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string[]> ReadNamesAsync(
        SqliteConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private sealed class ServerFixture : WebApplicationFactory<Program>
    {
        private readonly string _temporaryDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "CoreWatch-Atlas-Server-Tests",
                Guid.NewGuid().ToString("N"));

        public string DatabasePath =>
            Path.Combine(_temporaryDirectory, "atlas-tests.db");

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
}
// CoreWatch Atlas module: ServerApiTests.
