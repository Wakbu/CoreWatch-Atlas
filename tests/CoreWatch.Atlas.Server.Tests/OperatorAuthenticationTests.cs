using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class OperatorAuthenticationTests
{
    private const string TestPassword = "Atlas-test-password-2026!";

    [TestMethod]
    public async Task AnonymousRequestsCannotReadMonitoringData()
    {
        using var fixture = new OperatorFixture();
        using var client = fixture.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var agents = await client.GetAsync("/api/v1/agents");
        var status = await client.GetAsync("/api/v1/status");
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.AreEqual(HttpStatusCode.Unauthorized, agents.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, status.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
    }

    [TestMethod]
    public async Task LoginRequiresCsrfToken()
    {
        using var fixture = new OperatorFixture();
        using var client = fixture.CreateClient();
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync(
            "csrf-user",
            TestPassword,
            OperatorRoles.Viewer);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new OperatorLoginRequest("csrf-user", TestPassword));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task HttpsResponsesIncludeSecurityHeadersAndSecureCookie()
    {
        using var fixture = new OperatorFixture();
        using var client = fixture.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync(
            "secure-user",
            TestPassword,
            OperatorRoles.Viewer);

        var live = await client.GetAsync("/health/live");
        var login = await LoginAsync(client, "secure-user", TestPassword);

        Assert.AreEqual("nosniff", live.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.AreEqual("DENY", live.Headers.GetValues("X-Frame-Options").Single());

        StringAssert.Contains(
            live.Headers.GetValues("Content-Security-Policy").Single(),
            "frame-ancestors 'none'");
        StringAssert.Contains(
            login.Headers.GetValues("Set-Cookie").Single(
                value => value.StartsWith(
                    "CoreWatchAtlas.Operator=",
                    StringComparison.Ordinal)),
            "secure",
            StringComparison.OrdinalIgnoreCase);
    }
    [TestMethod]
    public async Task HttpIsRejectedWhenLoopbackExceptionIsDisabled()
    {
        using var fixture = new OperatorFixture(allowLoopbackHttp: false);
        using var client = fixture.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("http://localhost"),
                AllowAutoRedirect = false,
            });

        var response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }
    [TestMethod]
    public async Task ViewerCanReadButCannotListOperators()
    {
        using var fixture = new OperatorFixture();
        using var client = fixture.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync(
            "viewer",
            TestPassword,
            OperatorRoles.Viewer);

        var login = await LoginAsync(client, "viewer", TestPassword);
        var session =
            await login.Content.ReadFromJsonAsync<OperatorSessionResponse>();
        var agents = await client.GetAsync("/api/v1/agents");
        var archive = await SecurityTestClient.PostWithCsrfAsync(
            client,
            "/api/v1/agents/019c16a0-5f52-7000-8000-000000000001/archive");
        var operators = await client.GetAsync("/api/v1/operators");
        var logout = await SecurityTestClient.PostWithCsrfAsync(
            client,
            "/api/v1/auth/logout");
        var afterLogout = await client.GetAsync("/api/v1/agents");

        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        Assert.AreEqual(OperatorRoles.Viewer, session?.Role);
        StringAssert.Contains(
            login.Headers.GetValues("Set-Cookie").Single(),
            "httponly",
            StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(HttpStatusCode.OK, agents.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, operators.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, archive.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [TestMethod]
    public async Task OnlyAdministratorCanIssueOneTimeAgentInstallerToken()
    {
        using var fixture = new OperatorFixture();
        using var adminClient = fixture.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var viewerClient = fixture.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync("installer-admin", TestPassword, OperatorRoles.Administrator);
        await database.CreateOperatorAsync("installer-viewer", TestPassword, OperatorRoles.Viewer);
        await LoginAsync(adminClient, "installer-admin", TestPassword);
        await LoginAsync(viewerClient, "installer-viewer", TestPassword);
        var issued = await SecurityTestClient.PostWithCsrfAsync(adminClient, "/api/v1/agent-installers/token");
        var denied = await SecurityTestClient.PostWithCsrfAsync(viewerClient, "/api/v1/agent-installers/token");
        var token = await issued.Content.ReadFromJsonAsync<IssuedRegistrationToken>();

        Assert.AreEqual(HttpStatusCode.OK, issued.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        StringAssert.StartsWith(token?.Value, "catlas_reg_");
        Assert.IsNotNull(token);
        Assert.IsTrue(token.ExpiresAtUtc > fixture.TimeProvider.GetUtcNow());
    }
    [TestMethod]
    public async Task AdministratorCanListOperatorsWithoutPasswordHashes()
    {
        using var fixture = new OperatorFixture();
        using var client = fixture.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync(
            "administrator",
            TestPassword,
            OperatorRoles.Administrator);
        await LoginAsync(client, "administrator", TestPassword);

        var response = await client.GetAsync("/api/v1/operators");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(body, "administrator");
        Assert.IsFalse(
            body.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains(TestPassword, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FailedLoginsLockAccountAndStoreOnlyPasswordHash()
    {
        using var fixture = new OperatorFixture();
        using var client = fixture.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        await database.CreateOperatorAsync(
            "locked-user",
            TestPassword,
            OperatorRoles.Viewer);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await LoginAsync(
                client,
                "locked-user",
                "wrong-password-value");
            Assert.AreEqual(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await LoginAsync(client, "locked-user", TestPassword);
        Assert.AreEqual(HttpStatusCode.Unauthorized, locked.StatusCode);

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT password_hash, failed_login_count, lockout_end_utc,
                (SELECT COUNT(*) FROM operator_authentication_audit)
            FROM atlas_operators
            WHERE username = 'locked-user';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreNotEqual(TestPassword, reader.GetString(0));
        Assert.AreEqual(5, reader.GetInt32(1));
        Assert.IsFalse(reader.IsDBNull(2));
        Assert.AreEqual(6L, reader.GetInt64(3));
        await reader.DisposeAsync();
        await connection.DisposeAsync();

        fixture.TimeProvider.Advance(TimeSpan.FromMinutes(16));
        var unlocked = await LoginAsync(client, "locked-user", TestPassword);
        Assert.AreEqual(HttpStatusCode.OK, unlocked.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string username,
        string password) =>
        SecurityTestClient.PostAsJsonWithCsrfAsync(
            client,
            "/api/v1/auth/login",
            new OperatorLoginRequest(username, password));

    private sealed class OperatorFixture(
        bool allowLoopbackHttp = true) : WebApplicationFactory<Program>
    {
        private readonly string _temporaryDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "CoreWatch-Atlas-Operator-Tests",
                Guid.NewGuid().ToString("N"));

        public string DatabasePath =>
            Path.Combine(_temporaryDirectory, "atlas-operator-tests.db");

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
                            [$"{ServerSecurityOptions.SectionName}:DataProtectionKeyPath"] =
                                Path.Combine(_temporaryDirectory, "keys"),
                            [$"{ServerSecurityOptions.SectionName}:AllowLoopbackHttp"] =
                                allowLoopbackHttp.ToString(),
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
// CoreWatch Atlas module: OperatorAuthenticationTests.
