# CoreWatch-Atlas Server 기반

## 범위

Server/API 1단계는 중앙 서버의 실행 기반과 SQLite 저장소만 제공한다. 장비 등록·인증, Snapshot 수신·조회와 온라인 판정은 다음 단계에서 구현한다.

## 실행

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release
```

기본 데이터베이스는 서버 콘텐츠 루트 아래 `data/corewatch-atlas.db`에 생성된다. `Atlas:Server:DatabasePath` 설정 또는 환경 변수 `Atlas__Server__DatabasePath`로 절대·상대 경로를 지정할 수 있다. 상대 경로는 서버 콘텐츠 루트를 기준으로 해석한다.

## 상태 API

| 경로 | 용도 |
|---|---|
| `GET /health/live` | 프로세스 생존 확인 |
| `GET /health/ready` | SQLite 연결과 현재 스키마 확인 |
| `GET /api/v1/status` | 서비스 버전·시각·저장소 상태 확인 |

시작 시 데이터베이스 초기화에 실패하면 서버도 즉시 실패한다. 준비되지 않은 서버가 요청을 받지 않게 하는 fail-fast 정책이다.

## SQLite 스키마 v1

- `schema_migrations`: 적용된 스키마 버전
- `agents`: 다음 단계의 장비 등록·최근 접속 상태
- `snapshots`: Agent별 원본 JSON Snapshot 이력
- `ix_snapshots_agent_captured_at`: 장비·수집 시각 기준 이력 조회

외래 키, WAL 저널과 5초 busy timeout을 적용한다. 마이그레이션은 트랜잭션과 프로세스 내 잠금으로 중복 실행에 안전하다.

## 현재 보안 경계

상태 API에는 비밀값이나 데이터베이스 경로를 노출하지 않는다. 아직 인증·TLS와 Agent 수신 API가 없으므로 운영 인터넷에 직접 공개하는 단계가 아니다.
