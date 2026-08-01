using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CoreWatch.Atlas.Server;

internal static class ServerUpdateInstaller
{
    public static async Task<int> RunAsync(string handoffPath)
    {
        var handoff = JsonSerializer.Deserialize<ServerUpdateHandoff>(
            await File.ReadAllTextAsync(handoffPath))
            ?? throw new InvalidDataException("The server update handoff is empty.");
        try
        {
            await WaitForExitAsync(handoff.ParentProcessId);
            ServerUpdateWorker.VerifySha256(handoff.PackagePath, handoff.Sha256);
            ApplyPackage(handoff);
            return 0;
        }
        catch
        {
            RestoreBackup(handoff.InstallDirectory, handoff.BackupDirectory);
            throw;
        }
        finally
        {
            File.Delete(handoffPath);
        }
    }

    internal static void ApplyPackage(ServerUpdateHandoff handoff)
    {
        var extraction = Path.Combine(Path.GetDirectoryName(handoff.PackagePath)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraction);
        try
        {
            ExtractSafely(handoff.PackagePath, extraction);
            if (!File.Exists(Path.Combine(extraction, "CoreWatch.Atlas.Server.dll")))
            {
                throw new InvalidDataException("The update package does not contain the Server entry point.");
            }
            var installedVersion = System.Reflection.AssemblyName
                .GetAssemblyName(Path.Combine(extraction, "CoreWatch.Atlas.Server.dll"))
                .Version?.ToString(3);
            if (!string.Equals(
                installedVersion,
                Version.Parse(handoff.TargetVersion).ToString(3),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("The server update package version does not match the manifest.");
            }

            if (Directory.Exists(handoff.BackupDirectory))
            {
                Directory.Delete(handoff.BackupDirectory, true);
            }
            CopyDirectory(handoff.InstallDirectory, handoff.BackupDirectory);
            var existingSettings = Path.Combine(handoff.InstallDirectory, "appsettings.json");
            if (File.Exists(existingSettings))
            {
                File.Copy(existingSettings, Path.Combine(extraction, "appsettings.json"), true);
            }
            ClearDirectory(handoff.InstallDirectory);
            CopyDirectory(extraction, handoff.InstallDirectory);
        }
        finally
        {
            if (Directory.Exists(extraction))
            {
                Directory.Delete(extraction, true);
            }
        }
    }

    internal static void RestoreBackup(string installDirectory, string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }
        ClearDirectory(installDirectory);
        CopyDirectory(backupDirectory, installDirectory);
    }

    private static void ExtractSafely(string packagePath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var path = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('\\', '/')));
            if (!path.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The update package contains an unsafe path.");
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, true);
            if (OperatingSystem.IsLinux() && path.EndsWith(".so", StringComparison.Ordinal))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
    }

    private static void ClearDirectory(string path)
    {
        Directory.CreateDirectory(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static async Task WaitForExitAsync(int processId)
    {
        try { using var process = Process.GetProcessById(processId); await process.WaitForExitAsync(); }
        catch (ArgumentException) { }
    }
}
// CoreWatch Atlas module: ServerUpdateInstaller.
