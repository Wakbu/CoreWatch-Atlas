using Microsoft.Extensions.Options;
using System.Text;
using System.Net;
using System.Net.Mail;

namespace CoreWatch.Atlas.Server;

public sealed class AlertMaintenanceWorker(AtlasDatabase database, IOptions<ServerApiOptions> options, ILogger<AlertMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{await database.EvaluateOfflineAlertsAsync(TimeSpan.FromSeconds(options.Value.OfflineAfterSeconds),stoppingToken);}catch(Exception e)when(e is not OperationCanceledException){logger.LogError(e,"Alert maintenance failed.");}try{await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}catch(OperationCanceledException){break;}}}
}
public sealed class AlertNotificationWorker(AtlasDatabase database,IHttpClientFactory clients,IOptions<SmtpReportOptions> smtp,ILogger<AlertNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{foreach(var d in await database.GetPendingNotificationDeliveriesAsync(20,stoppingToken)){try{if(d.Url.Contains('@')&&!d.Url.Contains("://")){var x=smtp.Value;if(!x.Enabled)throw new InvalidOperationException("SMTP alert delivery is disabled.");using var mail=new MailMessage(x.From,d.Url,"CoreWatch Atlas alert",d.Payload);using var client=new SmtpClient(x.Host,x.Port){EnableSsl=true,Credentials=string.IsNullOrWhiteSpace(x.Username)?CredentialCache.DefaultNetworkCredentials:new NetworkCredential(x.Username,x.Password)};await client.SendMailAsync(mail,stoppingToken);await database.MarkNotificationDeliveryAsync(d.Id,true,null,stoppingToken);}else{using var response=await clients.CreateClient("atlas-alerts").PostAsync(d.Url,new StringContent(d.Payload,Encoding.UTF8,"application/json"),stoppingToken);await database.MarkNotificationDeliveryAsync(d.Id,response.IsSuccessStatusCode,response.IsSuccessStatusCode?null:$"HTTP {(int)response.StatusCode}",stoppingToken);}}catch(Exception e){await database.MarkNotificationDeliveryAsync(d.Id,false,e.Message,stoppingToken);}}}catch(Exception e)when(e is not OperationCanceledException){logger.LogError(e,"Alert notification delivery failed.");}try{await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken);}catch(OperationCanceledException){break;}}}
}
