using System.IO.Compression;
using System.Security.Cryptography;
using CoreWatch.Atlas.Server;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class ServerUpdateTests
{
    [TestMethod]
    public void ApplyPackageReplacesApplicationAndPreservesSettings()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var backup = Path.Combine(root, "backup");
            var package = Path.Combine(root, "server.zip");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "appsettings.json"), "{\"keep\":true}");
            File.WriteAllText(Path.Combine(install, "old.txt"), "old");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(
                    typeof(ServerUpdateInstaller).Assembly.Location,
                    "CoreWatch.Atlas.Server.dll");
                WriteEntry(archive, "wwwroot/new.txt", "new");
            }

            ServerUpdateInstaller.ApplyPackage(new ServerUpdateHandoff(
                typeof(ServerUpdateInstaller).Assembly.GetName().Version!.ToString(3), 0, package, install, backup,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)))));

            Assert.IsTrue(File.Exists(Path.Combine(install, "CoreWatch.Atlas.Server.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(install, "wwwroot", "new.txt")));
            Assert.AreEqual("{\"keep\":true}", File.ReadAllText(Path.Combine(install, "appsettings.json")));
            Assert.IsTrue(File.Exists(Path.Combine(backup, "old.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VerifySha256RejectsUnexpectedPackage()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "package");
            Assert.ThrowsExactly<InvalidDataException>(
                () => ServerUpdateWorker.VerifySha256(path, new string('0', 64)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
