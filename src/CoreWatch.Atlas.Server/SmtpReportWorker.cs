using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

internal sealed class SmtpReportWorker(AtlasDatabase database, IOptions<SmtpReportOptions> smtp, IOptions<ServerApiOptions> api, ILogger<SmtpReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { var now=DateTimeOffset.UtcNow;var next=new DateTimeOffset(now.Year,now.Month,now.Day,Math.Clamp(smtp.Value.SendHourUtc,0,23),0,0,TimeSpan.Zero);if(next<=now)next=next.AddDays(1);await Task.Delay(next-now,token);await SendAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError(e, "Scheduled SMTP report failed."); }
        }
    }
    private async Task SendAsync(CancellationToken token)
    {
        var x=smtp.Value;
        if(!x.Enabled||string.IsNullOrWhiteSpace(x.Host)||string.IsNullOrWhiteSpace(x.From)||string.IsNullOrWhiteSpace(x.To))return;
        var agents=await database.ListAgentsAsync(TimeSpan.FromSeconds(api.Value.OfflineAfterSeconds),false,token);
        var online=agents.Count(a=>a.Online);
        var alerts=await database.ListAlertsAsync(false,500,token);
        using var mail=new MailMessage(x.From,x.To,"CoreWatch Atlas daily report",$"Servers: {agents.Count}\nOnline: {online}\nOffline: {agents.Count-online}\nAlerts: {alerts.Count}");
        if(x.AttachCsv)mail.Attachments.Add(new Attachment(new MemoryStream(ReportExports.FleetCsv(agents,alerts)),"corewatch-atlas-daily.csv","text/csv"));
        if(x.AttachPdf)mail.Attachments.Add(new Attachment(new MemoryStream(ReportExports.FleetPdf(agents,alerts)),"corewatch-atlas-daily.pdf","application/pdf"));
        using var client=new SmtpClient(x.Host,x.Port){EnableSsl=x.EnableSsl,Credentials=string.IsNullOrWhiteSpace(x.Username)?CredentialCache.DefaultNetworkCredentials:new NetworkCredential(x.Username,x.Password)};
        await client.SendMailAsync(mail,token);
    }
}
// CoreWatch Atlas module: SmtpReportWorker.
