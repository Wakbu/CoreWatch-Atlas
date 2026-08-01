using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class RegistrationApiTests
{
    [TestMethod]
    public async Task IssuedTokenIsRandomAndStoredOnlyAsHash()
    {
        using var fixture = new RegistrationFixture();
        using var client = fixture.CreateClient();
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();

        var first = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(15));
        var second = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(15));

        StringAssert.StartsWith(first.Value, "catlas_reg_");
        Assert.AreNotEqual(first.Value, second.Value);

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT token_hash, typeof(token_hash)
            FROM registration_tokens
            WHERE token_hash = $tokenHash;
            """;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(first.Value));
        command.Parameters.Add("$tokenHash", SqliteType.Blob).Value = expectedHash;
        await using var reader = await command.ExecuteReaderAsync();

        Assert.IsTrue(await reader.ReadAsync());
        CollectionAssert.AreEqual(expectedHash, (byte[])reader.GetValue(0));
        Assert.AreEqual("blob", reader.GetString(1));
    }

    [TestMethod]
    public async Task RegistrationTokenCreatesPermanentAgentOnlyOnce()
    {
        using var fixture = new RegistrationFixture();
        using var client = fixture.CreateClient();
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        var token = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(15));
        var request = CreateRequest(token.Value);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/agents/register", request),
            client.PostAsJsonAsync("/api/v1/agents/register", request));
        var createdResponse = responses.Single(
            response => response.StatusCode == HttpStatusCode.Created);
        var registered =
            await createdResponse.Content.ReadFromJsonAsync<RegisteredAgent>();

        Assert.AreEqual(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.AreEqual(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        Assert.IsNotNull(registered);
        Assert.AreEqual(7, registered.AgentId.Version);
        Assert.AreEqual(
            $"/api/v1/agents/{registered.AgentId:D}",
            createdResponse.Headers.Location?.OriginalString);

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM agents
            WHERE agent_id = $agentId
              AND host_name = 'atlas-test-host';
            """;
        command.Parameters.AddWithValue("$agentId", registered.AgentId.ToString("D"));
        Assert.AreEqual(1L, (long)(await command.ExecuteScalarAsync())!);

        await using var credentialCommand = connection.CreateCommand();
        credentialCommand.CommandText =
            "SELECT credential_hash FROM agents WHERE agent_id = $agentId;";
        credentialCommand.Parameters.AddWithValue(
            "$agentId",
            registered.AgentId.ToString("D"));
        var storedCredentialHash =
            (byte[])(await credentialCommand.ExecuteScalarAsync())!;
        CollectionAssert.AreEqual(
            SHA256.HashData(Encoding.UTF8.GetBytes(registered.Credential)),
            storedCredentialHash);
    }

    [TestMethod]
    public async Task UnknownRegistrationTokenIsRejected()
    {
        using var fixture = new RegistrationFixture();
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/agents/register",
            CreateRequest("catlas_reg_unknown"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ExpiredRegistrationTokenIsRejected()
    {
        using var fixture = new RegistrationFixture();
        using var client = fixture.CreateClient();
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        var token = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(1));
        fixture.TimeProvider.Advance(TimeSpan.FromMinutes(2));

        var response = await client.PostAsJsonAsync(
            "/api/v1/agents/register",
            CreateRequest(token.Value));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidPayloadDoesNotConsumeToken()
    {
        using var fixture = new RegistrationFixture();
        using var client = fixture.CreateClient();
        var database = fixture.Services.GetRequiredService<AtlasDatabase>();
        var token = await database.CreateRegistrationTokenAsync(TimeSpan.FromMinutes(15));
        var invalidRequest = CreateRequest(token.Value) with { HostName = " " };

        var invalidResponse = await client.PostAsJsonAsync(
            "/api/v1/agents/register",
            invalidRequest);
        var validResponse = await client.PostAsJsonAsync(
            "/api/v1/agents/register",
            CreateRequest(token.Value));

        Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, validResponse.StatusCode);
    }

    private static AgentRegistrationRequest CreateRequest(string token) =>
        new(
            token,
            "atlas-test-host",
            "Linux",
            "x64",
            "0.1.0");

    private sealed class RegistrationFixture : WebApplicationFactory<Program>
    {
        private readonly string _temporaryDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "CoreWatch-Atlas-Registration-Tests",
                Guid.NewGuid().ToString("N"));

        public string DatabasePath =>
            Path.Combine(_temporaryDirectory, "atlas-registration-tests.db");

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
// CoreWatch Atlas module: RegistrationApiTests.
