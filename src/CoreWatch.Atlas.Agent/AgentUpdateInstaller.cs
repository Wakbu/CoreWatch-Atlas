using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace CoreWatch.Atlas.Agent;

internal static class AgentUpdateInstaller
{
    public static async Task<int> RunAsync(string handoffPath)
    {
        var handoff = JsonSerializer.Deserialize<AgentUpdateHandoff>(
            await File.ReadAllTextAsync(handoffPath))
            ?? throw new InvalidDataException("The update handoff is empty.");
        try
        {
            await WaitForExitAsync(handoff.ParentProcessId, TimeSpan.FromMinutes(2));
            ApplyPackage(
                handoff.PackagePath,
                handoff.InstallDirectory,
                handoff.BackupDirectory,
                handoff.TargetVersion);
            await WriteResultAsync(handoff, "succeeded", null);
            StartService(handoff.ServiceName);
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                RestoreBackup(handoff.InstallDirectory, handoff.BackupDirectory);
                await WriteResultAsync(handoff, "rolled_back", exception.Message);
                StartService(handoff.ServiceName);
            }
            catch (Exception rollbackException)
            {
                await WriteResultAsync(
                    handoff,
                    "failed",
                    $"{exception.Message}; rollback failed: {rollbackException.Message}");
            }
            return 1;
        }
        finally
        {
            try
            {
                File.Delete(handoffPath);
            }
            catch
            {
            }
        }
    }

    internal static void ApplyPackage(
        string packagePath,
        string installDirectory,
        string backupDirectory,
        string? expectedVersion = null)
    {
        var extractionDirectory = Path.Combine(
            Path.GetDirectoryName(packagePath)!, $"extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractionDirectory);
        try
        {
            ExtractSafely(packagePath, extractionDirectory);
            var entryPoint = Path.Combine(extractionDirectory, "CoreWatch.Atlas.Agent.dll");
            if (!File.Exists(entryPoint))
            {
                throw new InvalidDataException("The update package does not contain the Agent entry point.");
            }
            if (expectedVersion is not null
                && System.Reflection.AssemblyName.GetAssemblyName(entryPoint).Version?.ToString(3)
                    != Version.Parse(expectedVersion).ToString(3))
            {
                throw new InvalidDataException("The update package version does not match the manifest.");
            }
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, true);
            }
            CopyDirectory(installDirectory, backupDirectory, preserveData: true);
            ClearInstallDirectory(installDirectory);
            CopyDirectory(extractionDirectory, installDirectory, preserveData: false);
        }
        finally
        {
            if (Directory.Exists(extractionDirectory))
            {
                Directory.Delete(extractionDirectory, true);
            }
        }
    }

    internal static void ExtractSafely(string packagePath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
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
        }
    }

    internal static void RestoreBackup(string installDirectory, string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            throw new DirectoryNotFoundException("The Agent update backup is unavailable.");
        }
        ClearInstallDirectory(installDirectory);
        CopyDirectory(backupDirectory, installDirectory, preserveData: false);
    }

    private static void ClearInstallDirectory(string installDirectory)
    {
        Directory.CreateDirectory(installDirectory);
        foreach (var file in Directory.EnumerateFiles(installDirectory))
        {
            File.Delete(file);
        }
        foreach (var directory in Directory.EnumerateDirectories(installDirectory))
        {
            if (!string.Equals(
                    Path.GetFileName(directory), "data", StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination, bool preserveData)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (preserveData && relative.StartsWith("data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (preserveData && relative.StartsWith("data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static async Task WaitForExitAsync(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void StartService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || OperatingSystem.IsLinux())
        {
            return;
        }
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("sc.exe", $"start \"{serviceName}\"")
            : new ProcessStartInfo("systemctl", $"start {serviceName}");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("The Agent service restart command could not start.");
        process.WaitForExit(30000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The Agent service restart failed with exit code {process.ExitCode}.");
        }
    }

    private static Task WriteResultAsync(
        AgentUpdateHandoff handoff,
        string state,
        string? detail) =>
        File.WriteAllTextAsync(
            handoff.ResultPath,
            JsonSerializer.Serialize(
                new AgentUpdateResult(
                    handoff.DeploymentId, handoff.TargetVersion, state, detail)));
}
// CoreWatch Atlas module: AgentUpdateInstaller.
