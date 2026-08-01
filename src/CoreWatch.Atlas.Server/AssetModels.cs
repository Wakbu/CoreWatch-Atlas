namespace CoreWatch.Atlas.Server;

// 자산 메타데이터는 Agent가 보고하는 OS/버전과 별개로 사람이 관리하는 정보다.
// 재등록이나 Snapshot 갱신으로 담당자·메모·태그가 덮어써지지 않도록 별도 테이블에 둔다.
public sealed record AssetMetadata(Guid AgentId, string? Owner, string? Notes, string? Role, string? IpAddress, IReadOnlyList<string> Tags);
public sealed record AssetMetadataRequest(string? Owner, string? Notes, string? Role, string? IpAddress, IReadOnlyList<string>? Tags);
public sealed record AssetTag(long Id, string Name);
