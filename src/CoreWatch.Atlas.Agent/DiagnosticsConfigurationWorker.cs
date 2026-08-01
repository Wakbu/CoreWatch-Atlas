using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

public sealed class DiagnosticsConfigurationStore
{
    private DiagnosticsOptions current;
    public DiagnosticsConfigurationStore(IOptions<DiagnosticsOptions> initial)=>current=initial.Value;
    public DiagnosticsOptions Current=>Volatile.Read(ref current);
    public void Set(DiagnosticsOptions value)=>Volatile.Write(ref current,value);
}

internal sealed class DiagnosticsConfigurationWorker(AtlasServerClient server,DiagnosticsConfigurationStore store,ILogger<DiagnosticsConfigurationWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));do{try{var config=await server.GetDiagnosticsConfigurationAsync(stoppingToken);if(config is not null)store.Set(config);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}catch(Exception e){logger.LogWarning(e,"Diagnostics configuration poll failed.");}}while(await timer.WaitForNextTickAsync(stoppingToken));}
}
