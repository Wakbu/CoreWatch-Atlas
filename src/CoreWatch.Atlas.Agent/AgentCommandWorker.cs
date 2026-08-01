using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

internal sealed record AgentCommand(long Id,string CommandType,string Target);
internal sealed record AgentCommandStatus(string State,string? Detail);

internal sealed class AgentCommandWorker(AtlasServerClient server,DiagnosticsConfigurationStore options,ILogger<AgentCommandWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        do { try { var command=await server.GetPendingCommandAsync(stoppingToken);if(command is not null)await ExecuteCommandAsync(command,stoppingToken); } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}catch(Exception e){logger.LogWarning(e,"Agent command poll failed.");} } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task ExecuteCommandAsync(AgentCommand command,CancellationToken token)
    {
        if(command.CommandType!="restart-service"||!options.Current.Services.Contains(command.Target,StringComparer.OrdinalIgnoreCase)){await server.ReportCommandAsync(command.Id,"rejected","Service is not in the configured allowlist.",token);return;}
        try { await server.ReportCommandAsync(command.Id,"running",null,token);var start=new ProcessStartInfo(OperatingSystem.IsWindows()?"sc.exe":"systemctl"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};if(OperatingSystem.IsWindows()){start.ArgumentList.Add("stop");start.ArgumentList.Add(command.Target);}else{start.ArgumentList.Add("restart");start.ArgumentList.Add(command.Target);}using(var p=Process.Start(start)!){await p.WaitForExitAsync(token);if(p.ExitCode!=0)throw new InvalidOperationException((await p.StandardError.ReadToEndAsync(token)).Trim());}if(OperatingSystem.IsWindows()){await Task.Delay(1000,token);var startService=new ProcessStartInfo("sc.exe"){RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};startService.ArgumentList.Add("start");startService.ArgumentList.Add(command.Target);using var p=Process.Start(startService)!;await p.WaitForExitAsync(token);if(p.ExitCode!=0)throw new InvalidOperationException((await p.StandardError.ReadToEndAsync(token)).Trim());}await server.ReportCommandAsync(command.Id,"succeeded",null,token); }
        catch(Exception e){await server.ReportCommandAsync(command.Id,"failed",e.Message.Length>240?e.Message[..240]:e.Message,token);}
    }
}
// CoreWatch Atlas module: AgentCommandWorker.
