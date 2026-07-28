# CoreWatch-Atlas Server/API MVP

## 등록과 자격 증명

1. 운영자가 Server 로컬 CLI에서 일회성 등록 토큰을 발급한다.
2. Agent 정보를 `POST /api/v1/agents/register`에 보내면 UUIDv7 Agent ID와 자격 증명이 한 번 반환된다.
3. Server는 등록 토큰과 Agent 자격 증명의 SHA-256 해시만 저장한다.
4. Agent는 Snapshot 요청에 `Authorization: Bearer <credential>`을 사용한다.

자격 증명은 `POST /api/v1/agents/{agentId}/credentials/rotate`로 교체하고 `DELETE /api/v1/agents/{agentId}/credentials`로 폐기한다. 두 요청 모두 현재 자격 증명이 필요하다. 실패 인증, 교체와 폐기 이벤트는 비밀값 없이 `authentication_audit`에 기록한다.

## Snapshot API

| 메서드와 경로 | 인증 | 용도 |
|---|---|---|
| `POST /api/v1/agents/{agentId}/snapshots` | Agent Bearer | Snapshot 수신 |
| `GET /api/v1/agents` | 없음 | 전체 장비·최신 상태 |
| `GET /api/v1/agents/{agentId}` | 없음 | 장비 하나의 최신 상태 |
| `GET /api/v1/agents/{agentId}/snapshots` | 없음 | 기간별 이력 |

이력 조회는 `fromUtc`, `toUtc`, `limit`을 지원한다. 기본 범위는 최근 24시간, 기본 제한은 200개이며 최대 1,000개다. 온라인 상태는 마지막 수신 시각이 `Atlas:ServerApi:OfflineAfterSeconds` 이내인지로 계산한다.

현재 조회 API에는 운영자·사용자 인증이 없다. Web 인증이 구현되기 전에는 loopback 또는 신뢰된 사설망에서만 Server를 운영해야 한다.

## 보존 설정

```json
{
  "Atlas": {
    "ServerApi": {
      "OfflineAfterSeconds": 45,
      "SnapshotRetentionDays": 30,
      "CleanupIntervalMinutes": 60
    }
  }
}
```

백그라운드 정리 작업은 수신 시각 기준 보존 기간이 지난 Snapshot을 삭제한다. Snapshot 요청 크기는 2 MiB로 제한한다.

## Agent 전송 설정

Agent의 `Atlas:ServerTransmission`을 설정하거나 같은 이름의 환경 변수를 사용한다.

```json
{
  "Enabled": true,
  "BaseUrl": "https://atlas.example.internal/",
  "AgentId": "서버가 발급한 UUID",
  "Credential": "서버가 한 번 반환한 자격 증명"
}
```

운영 환경에서는 자격 증명을 저장소나 배포 파일에 커밋하지 않고 환경 변수 또는 별도 비밀 저장소로 주입한다. 서버 전송 실패는 로컬 수집·JSON·Prometheus 출력을 중단시키지 않으며 다음 수집 주기에 재시도한다.
