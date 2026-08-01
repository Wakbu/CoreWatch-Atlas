namespace CoreWatch.Atlas.Agent;

// 운영자가 지정한 서비스만 확인한다. 전체 서비스/프로세스 목록은 개인정보와 전송량 문제로
// 기본 수집 대상이 아니다.
public sealed class DiagnosticsOptions
{
    public const string SectionName = "Atlas:Diagnostics";
    public string[] Services { get; set; } = [];

    // 프로세스와 컨테이너는 이름만 확인하며 명령행 인수나 환경 변수는 수집하지 않는다.
    public string[] Processes { get; set; } = [];
    public string[] Containers { get; set; } = [];
    public string[] Urls { get; set; } = [];
    public string[] TcpEndpoints { get; set; } = [];
    public string[] PingTargets { get; set; } = [];
    public string[] BackupPaths { get; set; } = [];
}
// CoreWatch Atlas module: DiagnosticsOptions.
