namespace CoreWatch.Atlas.Server;

// SMTP 자격 증명은 appsettings에 직접 넣지 않고 환경 변수나 배포 비밀 저장소에서 주입한다.
// Enabled 기본값을 false로 둬 설정이 없는 설치에서 외부 전송이 발생하지 않게 한다.
public sealed class SmtpReportOptions
{
    public const string SectionName = "Atlas:SmtpReport";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
