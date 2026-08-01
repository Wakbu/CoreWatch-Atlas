namespace CoreWatch.Atlas.Server;

// 유지보수 창은 예정된 작업 중 발생하는 알림을 억제하기 위한 시간 범위다.
// 경고 원본은 계속 기록하므로 작업 후에도 장애 이력을 잃지 않는다.
public sealed record MaintenanceWindow(long Id, string Name, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, Guid? AgentId=null, long? GroupId=null);
public sealed record MaintenanceWindowRequest(string Name, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, Guid? AgentId=null, long? GroupId=null);
// CoreWatch Atlas module: MaintenanceModels.
