using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

internal sealed class SmtpReportWorker(AtlasDatabase database, IOptions<SmtpReportOptions> smtp, IOptions<ServerApiOptions> api, ILogger<SmtpReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        // 시작 직후 메일을 보내지 않고 하루 단위로 동작한다. 재시작이 많은 환경에서 중복 발송을 피한다.
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromDays(1), token); await SendAsync(token); }
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
        using var mail=new MailMessage(x.From,x.To,"CoreWatch Atlas daily report",$"Servers: {agents.Count}\nOnline: {online}\nOffline: {agents.Count-online}");
        using var client=new SmtpClient(x.Host,x.Port){EnableSsl=true,Credentials=string.IsNullOrWhiteSpace(x.Username)?CredentialCache.DefaultNetworkCredentials:new NetworkCredential(x.Username,x.Password)};
        await client.SendMailAsync(mail,token);
    }
}
