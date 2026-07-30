using Microsoft.Extensions.Options;
using System.Text;

namespace CoreWatch.Atlas.Server;

public sealed class AlertMaintenanceWorker(AtlasDatabase database, IOptions<ServerApiOptions> options, ILogger<AlertMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{await database.EvaluateOfflineAlertsAsync(TimeSpan.FromSeconds(options.Value.OfflineAfterSeconds),stoppingToken);}catch(Exception e)when(e is not OperationCanceledException){logger.LogError(e,"Alert maintenance failed.");}try{await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}catch(OperationCanceledException){break;}}}
}
public sealed class AlertNotificationWorker(AtlasDatabase database,IHttpClientFactory clients,ILogger<AlertNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{foreach(var d in await database.GetPendingNotificationDeliveriesAsync(20,stoppingToken)){try{using var response=await clients.CreateClient("atlas-alerts").PostAsync(d.Url,new StringContent(d.Payload,Encoding.UTF8,"application/json"),stoppingToken);await database.MarkNotificationDeliveryAsync(d.Id,response.IsSuccessStatusCode,response.IsSuccessStatusCode?null:$"HTTP {(int)response.StatusCode}",stoppingToken);}catch(Exception e){await database.MarkNotificationDeliveryAsync(d.Id,false,e.Message,stoppingToken);}}}catch(Exception e)when(e is not OperationCanceledException){logger.LogError(e,"Alert notification delivery failed.");}try{await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken);}catch(OperationCanceledException){break;}}}
}