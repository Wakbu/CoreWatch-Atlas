using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CoreWatch.Atlas.Agent.Tests;

[TestClass]
public sealed class AgentCredentialStoreTests
{
    [TestMethod]
    public void CredentialsRoundTripWithoutPlaintextOnDisk()
    {
        using var fixture = new CredentialStoreFixture();
        var credentials = new StoredAgentCredentials(
            "https://atlas.example.test/",
            Guid.Parse("019c16a0-5f52-7000-8000-000000000002"),
            "catlas_agent_protected-test-secret");

        fixture.Store.Save(credentials);
        var loaded = new AgentCredentialStore(
            fixture.Configuration,
            fixture.Environment).Load();
        var raw = File.ReadAllBytes(
            Path.Combine(fixture.StorePath, "credentials.protected"));

        Assert.AreEqual(credentials, loaded);
        Assert.IsFalse(
            Encoding.UTF8.GetString(raw).Contains(
                credentials.Credential,
                StringComparison.Ordinal));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.AreEqual(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(
                    Path.Combine(
                        fixture.StorePath,
                        "credentials.protected")));
        }
    }

    [TestMethod]
    public void TamperedCredentialFileFailsClosed()
    {
        using var fixture = new CredentialStoreFixture();
        fixture.Store.Save(
            new StoredAgentCredentials(
                "https://atlas.example.test/",
                Guid.Parse("019c16a0-5f52-7000-8000-000000000003"),
                "catlas_agent_protected-test-secret"));
        var path = Path.Combine(
            fixture.StorePath,
            "credentials.protected");
        var payload = File.ReadAllBytes(path);
        payload[^1] ^= 0xff;
        File.WriteAllBytes(path, payload);

        Assert.Throws<InvalidOperationException>(() => fixture.Store.Load());
    }

    private sealed class CredentialStoreFixture : IDisposable
    {
        private readonly string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "CoreWatch-Atlas-Credential-Tests",
            Guid.NewGuid().ToString("N"));

        public CredentialStoreFixture()
        {
            StorePath = Path.Combine(temporaryDirectory, "credentials");
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [$"{AgentCredentialStoreOptions.SectionName}:Path"] =
                            StorePath,
                    })
                .Build();
            Environment = new TestHostEnvironment(temporaryDirectory);
            Store = new AgentCredentialStore(Configuration, Environment);
        }

        public string StorePath { get; }

        public IConfiguration Configuration { get; }

        public IHostEnvironment Environment { get; }

        public AgentCredentialStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Development;

        public string ApplicationName { get; set; } =
            "CoreWatch.Atlas.Agent.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
// CoreWatch Atlas module: AgentCredentialStoreTests.
