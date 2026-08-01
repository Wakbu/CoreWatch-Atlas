namespace CoreWatch.Atlas.Server;

// 그룹은 서버의 물리적 위치나 역할(예: 운영 DB, 개발 웹)을 표현한다.
// Agent 자체의 식별 정보와 분리해 두면 서버 재등록 후에도 분류 정책을 독립적으로 관리할 수 있다.
public sealed record ServerGroup(long Id, string Name, string? Description, int MemberCount);
public sealed record ServerGroupRequest(string Name, string? Description);
// 보고서는 원본 Snapshot을 다시 전달하지 않고, 기간별 운영 판단에 필요한 집계값만 제공한다.
// 원본 데이터는 기존 Snapshot API에서 계속 조회할 수 있어 응답 크기와 개인정보 노출 범위를 제한한다.
public sealed record ServerReport(
    Guid AgentId, string HostName, DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    int SnapshotCount, double AvailabilityPercent, MetricReport Cpu,
    MetricReport Memory, MetricReport Disk, IReadOnlyList<AlertRecord> Alerts);
public sealed record MetricReport(double? Average, double? Maximum, double? Latest);

// 추세 계산은 관측치가 부족하거나 사용량이 감소하는 경우 예측을 만들지 않는다.
// 근거 없는 날짜를 표시하는 것보다 "예측 불가"가 운영 판단에 안전하다.
public sealed record CapacityForecast(Guid AgentId, string HostName, double? CurrentUsedPercent, double? DailyGrowthPercent, double? DaysUntilFull);
