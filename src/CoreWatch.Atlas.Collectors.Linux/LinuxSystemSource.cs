using System.Reflection;
using System.Runtime.InteropServices;

namespace CoreWatch.Atlas.Collectors.Linux;

internal interface ILinuxSystemSource
{
    string HostName { get; }
    string OperatingSystem { get; }
    string Architecture { get; }
    string AgentVersion { get; }
    ValueTask<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
    IReadOnlyList<LinuxFileSystem> GetFileSystems();
}

internal sealed record LinuxFileSystem(
    string Id,
    string MountPoint,
    ulong TotalBytes,
    ulong AvailableBytes);

internal sealed class LinuxSystemSource : ILinuxSystemSource
{
    public string HostName => Environment.MachineName;
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string Architecture => RuntimeInformation.OSArchitecture.ToString();
    public string AgentVersion =>
        typeof(LinuxSystemSource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(LinuxSystemSource).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public async ValueTask<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken) =>
        await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

    public IReadOnlyList<LinuxFileSystem> GetFileSystems()
    {
        var fileSystems = new List<LinuxFileSystem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.TotalSize <= 0)
                {
                    continue;
                }

                fileSystems.Add(new LinuxFileSystem(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    checked((ulong)drive.TotalSize),
                    checked((ulong)drive.AvailableFreeSpace)));
            }
            catch (IOException)
            {
                // A mount can disappear while DriveInfo is enumerating it.
            }
            catch (UnauthorizedAccessException)
            {
                // Restricted mounts are omitted from the optional collection.
            }
        }

        return fileSystems;
    }
}
