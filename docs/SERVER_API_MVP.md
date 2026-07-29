# CoreWatch-Atlas Server/API MVP

## 등록과 자격 증명

1. 운영자가 Server 로컬 CLI에서 일회성 등록 토큰을 발급한다.
2. Agent 정보를 `POST /api/v1/agents/register`에 보내면 UUIDv7 Agent ID와 자격 증명이 한 번 반환된다.
3. Server는 등록 토큰과 Agent 자격 증명의 SHA-256 해시만 저장한다.
4. Agent는 Snapshot 요청에 `Authorization: Bearer <credential>`을 사용한다.

자격 증명은 `POST /api/v1/agents/{agentId}/credentials/rotate`로 교체하고 `DELETE /api/v1/agents/{agentId}/credentials`로 폐기한다. 두 요청 모두 현재 자격 증명이 필요하다. 실패 인증, 교체와 폐기 이벤트는 비밀값 없이 `authentication_audit`에 기록한다.

## 운영자 인증과 권한

Web은 HttpOnly·SameSite 쿠키로 로그인한다. Agent Bearer 인증과 운영자 인증은 서로 분리된다.

| 메서드와 경로 | 권한 | 용도 |
|---|---|---|
| `GET /api/v1/auth/csrf` | 익명 | 로그인·로그아웃용 CSRF 토큰 |
| `POST /api/v1/auth/login` | 익명·CSRF | 운영자 로그인 |
| `GET /api/v1/auth/me` | Viewer·Administrator | 현재 세션 |
| `POST /api/v1/auth/logout` | Viewer·Administrator·CSRF | 로그아웃 |
| `GET /api/v1/operators` | Administrator | 비밀번호 해시를 제외한 운영자 목록 |

공개 회원가입과 HTTP 계정 생성 API는 없다. 계정은 Server 로컬 CLI에서 생성한다. 로그인 성공·실패·잠금과 로그아웃은 비밀값 없이 감사 테이블에 기록한다.

## Agent 수명주기

`Administrator`만 다음 API를 사용할 수 있으며, 모든 변경 요청은 CSRF 토큰이 필요하다.

| 메서드와 경로 | 용도 |
|---|---|
| `POST /api/v1/agents/{agentId}/archive` | Agent를 보관하고 현재 자격 증명을 폐기 |
| `POST /api/v1/agents/{agentId}/restore` | 보관 해제. Agent는 새 등록으로 자격 증명을 다시 발급해야 함 |
| `DELETE /api/v1/agents/{agentId}` | `{"deleteSnapshots":false}`면 Snapshot을 보관하고 Agent만 삭제, `true`면 이력도 삭제 |

기본 `GET /api/v1/agents`는 보관 Agent를 제외한다. Administrator는 `?includeArchived=true`로 보관 항목을 함께 조회할 수 있다. 보관·복원·삭제는 `agent_lifecycle_audit`에 감사 기록을 남긴다.
## Snapshot API

| 메서드와 경로 | 인증 | 용도 |
|---|---|---|
| `POST /api/v1/agents/{agentId}/snapshots` | Agent Bearer | Snapshot 수신 |
| `GET /api/v1/agents` | Viewer·Administrator | 전체 장비·최신 상태 |
| `GET /api/v1/agents/{agentId}` | Viewer·Administrator | 장비 하나의 최신 상태 |
| `GET /api/v1/agents/{agentId}/snapshots` | Viewer·Administrator | 기간별 이력 |

이력 조회는 `fromUtc`, `toUtc`, `limit`을 지원한다. 기본 범위는 최근 24시간, 기본 제한은 200개이며 최대 1,000개다. 온라인 상태는 마지막 수신 시각이 `Atlas:ServerApi:OfflineAfterSeconds` 이내인지로 계산한다.

Server는 외부 HTTP를 거부하고 HTTPS를 요구한다. 개발용 loopback HTTP 예외는 운영에서 꺼야 한다. 인증서, CSRF와 비밀 저장 절차는 [HTTPS·비밀정보 운영](SECURITY_DEPLOYMENT.md)을 따른다.

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

운영 환경에서는 등록 CLI가 만든 보호 저장소를 사용한다. 자격 증명을 설정 파일이나 배포 파일에 커밋하지 않는다. 서버 전송 실패는 로컬 수집·JSON·Prometheus 출력을 중단시키지 않으며 다음 수집 주기에 재시도한다.
