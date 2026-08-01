using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class SchemaMigrationTests
{
    [TestMethod]
    public async Task VersionTwoDatabaseUpgradesToCredentialSchema()
    {
        using var fixture = new VersionTwoFixture();
        using var client = fixture.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        ready.EnsureSuccessStatusCode();

        await using var connection =
            new SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        var columns = await ReadNamesAsync(connection, "PRAGMA table_info(agents);", 1);
        var tables = await ReadNamesAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table';",
            0);
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(version) FROM schema_migrations;";

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "credential_hash",
                "credential_created_at_utc",
                "credential_revoked_at_utc",
            },
            columns);
        CollectionAssert.Contains(tables, "authentication_audit");
        CollectionAssert.Contains(tables, "atlas_operators");
        CollectionAssert.Contains(tables, "operator_authentication_audit");
        CollectionAssert.Contains(tables, "asset_tags");
        CollectionAssert.Contains(tables, "alert_actions");
        CollectionAssert.Contains(tables, "api_tokens");
        CollectionAssert.Contains(tables, "agent_commands");
        CollectionAssert.Contains(tables, "agent_diagnostic_config");
        var ruleColumns = await ReadNamesAsync(connection, "PRAGMA table_info(alert_rules);", 1);
        CollectionAssert.Contains(ruleColumns, "duration_seconds");
        CollectionAssert.Contains(ruleColumns, "renotify_minutes");
        Assert.AreEqual((long)AtlasDatabase.CurrentSchemaVersion, (long)(await versionCommand.ExecuteScalarAsync())!);
    }

    private static async Task<string[]> ReadNamesAsync(
        SqliteConnection connection,
        string sql,
        int ordinal)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(ordinal));
        }

        return names.ToArray();
    }

    private sealed class VersionTwoFixture : WebApplicationFactory<Program>
    {
        private readonly string _temporaryDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "CoreWatch-Atlas-Migration-Tests",
                Guid.NewGuid().ToString("N"));
        private bool _created;

        public string DatabasePath =>
            Path.Combine(_temporaryDirectory, "atlas-v2.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    CreateVersionTwoDatabase();
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

        private void CreateVersionTwoDatabase()
        {
            if (_created)
            {
                return;
            }

            Directory.CreateDirectory(_temporaryDirectory);
            using var connection =
                new SqliteConnection($"Data Source={DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at_utc TEXT NOT NULL
                );
                INSERT INTO schema_migrations VALUES (1, '2026-07-28T00:00:00Z');
                INSERT INTO schema_migrations VALUES (2, '2026-07-28T00:00:00Z');

                CREATE TABLE agents (
                    agent_id TEXT PRIMARY KEY,
                    host_name TEXT NOT NULL,
                    operating_system TEXT NOT NULL,
                    architecture TEXT NOT NULL,
                    agent_version TEXT NOT NULL,
                    registered_at_utc TEXT NOT NULL,
                    last_seen_at_utc TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
            _created = true;
        }
    }
}
// CoreWatch Atlas module: SchemaMigrationTests.
