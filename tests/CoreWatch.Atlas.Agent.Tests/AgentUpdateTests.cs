using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent.Tests;

[TestClass]
public sealed class AgentUpdateTests
{
    [TestMethod]
    public void ManifestRejectsNonHttpsAndMalformedHash()
    {
        var server = new Uri("https://atlas.example.test/");
        Assert.IsFalse(AtlasServerClient.TryValidateManifest(
            new AgentUpdateManifest(1, "2.0.0", "http://example.test/agent.zip", new string('a', 64)),
            server,
            out _));
        Assert.IsFalse(AtlasServerClient.TryValidateManifest(
            new AgentUpdateManifest(1, "2.0.0", "https://example.test/agent.zip", "bad"),
            server,
            out _));
    }

    [TestMethod]
    public async Task DownloadDeletesTrustWhenHashDoesNotMatch()
    {
        using var client = new HttpClient(new PackageHandler([1, 2, 3]));
        var server = new AtlasServerClient(
            client,
            Options.Create(new ServerTransmissionOptions
            {
                Enabled = true,
                BaseUrl = "https://atlas.example.test/",
                AgentId = "019c16a0-5f52-7000-8000-000000000001",
                Credential = "credential",
            }));
        var path = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                server.DownloadUpdateAsync(
                    new AgentUpdateManifest(
                        1, "2.0.0", "https://atlas.example.test/agent.zip", new string('0', 64)),
                    path,
                    CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void InstallerReplacesApplicationAndPreservesData()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(Path.Combine(install, "data"));
            File.WriteAllText(Path.Combine(install, "old.txt"), "old");
            File.WriteAllText(Path.Combine(install, "data", "credential"), "secret");
            var package = Path.Combine(root, "agent.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "CoreWatch.Atlas.Agent.dll", "new");
                WriteEntry(archive, "new.txt", "new");
            }

            AgentUpdateInstaller.ApplyPackage(package, install, backup);

            Assert.IsFalse(File.Exists(Path.Combine(install, "old.txt")));
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(install, "new.txt")));
            Assert.AreEqual("secret", File.ReadAllText(Path.Combine(install, "data", "credential")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(backup, "old.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void InstallerRejectsArchiveTraversal()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var package = Path.Combine(root, "unsafe.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "../outside.txt", "unsafe");
            }
            Assert.Throws<InvalidDataException>(
                () => AgentUpdateInstaller.ExtractSafely(package, Path.Combine(root, "extract")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void InstallerRollbackRestoresPreviousFilesAndPreservesData()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(Path.Combine(install, "data"));
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(install, "new.txt"), "new");
            File.WriteAllText(Path.Combine(install, "data", "credential"), "secret");
            File.WriteAllText(Path.Combine(backup, "old.txt"), "old");

            AgentUpdateInstaller.RestoreBackup(install, backup);

            Assert.IsFalse(File.Exists(Path.Combine(install, "new.txt")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(install, "old.txt")));
            Assert.AreEqual("secret", File.ReadAllText(Path.Combine(install, "data", "credential")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CoreWatch-Atlas-Update-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(value);
    }

    private sealed class PackageHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
            });
    }
}
// CoreWatch Atlas module: AgentUpdateTests.
