using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

internal sealed class AgentUpdateWorker(
    AtlasServerClient server,
    IOptions<AutomaticUpdateOptions> options,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ILogger<AgentUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }
        var statePath = Path.GetFullPath(
            Path.IsPathRooted(settings.StatePath)
                ? settings.StatePath
                : Path.Combine(environment.ContentRootPath, settings.StatePath));
        Directory.CreateDirectory(statePath);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportPreviousResultAsync(statePath, stoppingToken);
                var manifest = await server.GetPendingUpdateAsync(stoppingToken);
                if (manifest is not null
                    && Version.TryParse(manifest.Version, out var target)
                    && target > typeof(Program).Assembly.GetName().Version)
                {
                    await StageAndHandoffAsync(manifest, statePath, settings, stoppingToken);
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Agent automatic update check failed.");
            }
            await Task.Delay(settings.CheckInterval, stoppingToken);
        }
    }

    private async Task StageAndHandoffAsync(
        AgentUpdateManifest manifest,
        string statePath,
        AutomaticUpdateOptions settings,
        CancellationToken token)
    {
        var deploymentPath = Path.Combine(statePath, manifest.DeploymentId.ToString());
        Directory.CreateDirectory(deploymentPath);
        var packagePath = Path.Combine(deploymentPath, "agent.zip.partial");
        try
        {
            await server.ReportUpdateStatusAsync(manifest.DeploymentId, "downloading", null, token);
            await server.DownloadUpdateAsync(manifest, packagePath, token);
            var finalPackagePath = Path.Combine(deploymentPath, "agent.zip");
            File.Move(packagePath, finalPackagePath, true);
            await server.ReportUpdateStatusAsync(manifest.DeploymentId, "staged", null, token);

            var resultPath = Path.Combine(statePath, "last-result.json");
            var handoff = new AgentUpdateHandoff(
                manifest.DeploymentId,
                manifest.Version,
                Environment.ProcessId,
                finalPackagePath,
                environment.ContentRootPath,
                Path.Combine(deploymentPath, "backup"),
                ResolveServiceName(settings.ServiceName),
                resultPath);
            var handoffPath = OperatingSystem.IsLinux()
                ? Path.Combine(statePath, "pending-handoff.json")
                : Path.Combine(deploymentPath, "handoff.json");
            await File.WriteAllTextAsync(handoffPath, JsonSerializer.Serialize(handoff), token);
            await server.ReportUpdateStatusAsync(manifest.DeploymentId, "applying", null, token);
            if (!OperatingSystem.IsLinux())
            {
                var helperPath = Path.Combine(deploymentPath, "helper");
                CopyHelper(environment.ContentRootPath, helperPath);
                StartHelper(helperPath, handoffPath);
            }
            Environment.ExitCode = 75;
            lifetime.StopApplication();
        }
        catch (Exception exception)
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
            await server.ReportUpdateStatusAsync(
                manifest.DeploymentId, "failed", exception.Message, token);
            throw;
        }
    }

    private async Task ReportPreviousResultAsync(string statePath, CancellationToken token)
    {
        var resultPath = Path.Combine(statePath, "last-result.json");
        if (!File.Exists(resultPath))
        {
            return;
        }
        var result = JsonSerializer.Deserialize<AgentUpdateResult>(
            await File.ReadAllTextAsync(resultPath, token));
        if (result is not null)
        {
            var currentVersion =
                typeof(Program).Assembly.GetName().Version?.ToString(3);
            var state = result.State == "succeeded"
                && !string.Equals(
                    currentVersion, Version.Parse(result.TargetVersion).ToString(3),
                    StringComparison.Ordinal)
                    ? "failed"
                    : result.State;
            var detail = state == "failed" && result.State == "succeeded"
                ? $"Restarted Agent version {currentVersion} does not match {result.TargetVersion}."
                : result.Detail;
            await server.ReportUpdateStatusAsync(
                result.DeploymentId, state, detail, token);
        }
        File.Delete(resultPath);
    }

    private static void CopyHelper(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
    }

    private static void StartHelper(string helperPath, string handoffPath)
    {
        var assemblyPath = Path.Combine(helperPath, "CoreWatch.Atlas.Agent.dll");
        var startInfo = new ProcessStartInfo(
            "dotnet", $"\"{assemblyPath}\" --apply-agent-update \"{handoffPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = helperPath,
        };
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Agent update helper could not start.");
    }

    private static string ResolveServiceName(string configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? OperatingSystem.IsWindows()
                ? "CoreWatchAtlasAgent"
                : "corewatch-atlas-agent"
            : configured;
}
// CoreWatch Atlas module: AgentUpdateWorker.
