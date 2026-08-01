using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

internal sealed class ServerUpdateWorker(
    IHttpClientFactory clients,
    IOptionsMonitor<ServerUpdateOptions> options,
    GitHubReleaseCatalog releases,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ILogger<ServerUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = options.CurrentValue;
                if (!settings.Enabled)
                {
                    var published = await releases.GetLatestAsync(stoppingToken);
                    if (published is not null)
                    {
                        settings = new ServerUpdateOptions
                        {
                            Enabled = true,
                            Version = published.Version,
                            PackageUrl = published.ServerPackageUrl,
                            Sha256 = published.ServerSha256,
                            StatePath = settings.StatePath,
                            CheckIntervalMinutes = settings.CheckIntervalMinutes,
                        };
                    }
                }
                if (settings.Enabled && IsNewer(settings.Version))
                {
                    await StageAndStopAsync(settings, stoppingToken);
                    return;
                }

                await Task.Delay(
                    TimeSpan.FromMinutes(Math.Clamp(settings.CheckIntervalMinutes, 5, 1440)),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Server automatic update check failed.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task StageAndStopAsync(ServerUpdateOptions settings, CancellationToken token)
    {
        var statePath = Path.GetFullPath(Path.IsPathRooted(settings.StatePath)
            ? settings.StatePath
            : Path.Combine(environment.ContentRootPath, settings.StatePath));
        Directory.CreateDirectory(statePath);
        var handoffPath = Path.Combine(statePath, "pending-server-update.json");
        if (File.Exists(handoffPath))
        {
            return;
        }

        var packagePath = Path.Combine(statePath, $"server-{settings.Version}.zip");
        var partialPath = packagePath + ".partial";
        try
        {
            using var response = await clients.CreateClient("atlas-server-update")
                .GetAsync(settings.PackageUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var destination = File.Create(partialPath))
            {
                await source.CopyToAsync(destination, token);
            }

            VerifySha256(partialPath, settings.Sha256);
            File.Move(partialPath, packagePath, true);
            var handoff = new ServerUpdateHandoff(
                settings.Version,
                Environment.ProcessId,
                packagePath,
                environment.ContentRootPath,
                Path.Combine(statePath, $"backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"),
                settings.Sha256);
            await File.WriteAllTextAsync(handoffPath, JsonSerializer.Serialize(handoff), token);
            Environment.ExitCode = 75;
            lifetime.StopApplication();
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
    }

    private static bool IsNewer(string version) =>
        Version.TryParse(version, out var target)
        && target > typeof(ServerUpdateWorker).Assembly.GetName().Version;

    internal static void VerifySha256(string path, string expected)
    {
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The server update package SHA-256 does not match.");
        }
    }
}
