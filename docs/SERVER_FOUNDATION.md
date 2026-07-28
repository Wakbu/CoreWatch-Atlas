# CoreWatch-Atlas Server 기반

## 범위

Server/API 기반은 중앙 서버, SQLite 저장소와 일회성 등록 토큰을 사용한 영구 Agent ID 발급을 제공한다. Agent 자격 증명 인증·교체·폐기와 Snapshot 수신은 다음 단계에서 구현한다.

## 실행

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release
```

기본 데이터베이스는 서버 콘텐츠 루트 아래 `data/corewatch-atlas.db`에 생성된다. `Atlas:Server:DatabasePath` 설정 또는 환경 변수 `Atlas__Server__DatabasePath`로 절대·상대 경로를 지정할 수 있다. 상대 경로는 서버 콘텐츠 루트를 기준으로 해석한다.

운영자는 서버가 정지된 상태에서 다음 명령으로 기본 15분짜리 일회성 등록 토큰을 발급한다.

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release -- --create-registration-token
```

유효 시간은 `Atlas:Registration:TokenLifetimeMinutes`로 1~1440분 범위에서 설정한다. 토큰 원문은 명령 결과에서 한 번만 보여 주며 SQLite에는 SHA-256 해시만 저장한다.

## 상태·등록 API

| 경로 | 용도 |
|---|---|
| `GET /health/live` | 프로세스 생존 확인 |
| `GET /health/ready` | SQLite 연결과 현재 스키마 확인 |
| `GET /api/v1/status` | 서비스 버전·시각·저장소 상태 확인 |
| `POST /api/v1/agents/register` | 유효한 일회성 토큰을 소비하고 UUIDv7 Agent ID 발급 |

시작 시 데이터베이스 초기화에 실패하면 서버도 즉시 실패한다. 준비되지 않은 서버가 요청을 받지 않게 하는 fail-fast 정책이다.

## SQLite 스키마 v2

- `schema_migrations`: 적용된 스키마 버전
- `agents`: 영구 Agent ID와 등록 당시 장비 정보
- `snapshots`: Agent별 원본 JSON Snapshot 이력
- `registration_tokens`: 토큰 해시, 만료·소비 시각과 발급된 Agent ID
- `ix_snapshots_agent_captured_at`: 장비·수집 시각 기준 이력 조회

외래 키, WAL 저널과 5초 busy timeout을 적용한다. 마이그레이션과 등록 처리는 트랜잭션으로 중복 실행과 토큰 재사용을 방지한다.

## 현재 보안 경계

상태 API에는 비밀값이나 데이터베이스 경로를 노출하지 않는다. 등록 토큰 발급은 HTTP API로 노출하지 않고 로컬 CLI에서만 수행한다. 아직 Agent 자격 증명·TLS와 Snapshot 수신 API가 없으므로 운영 인터넷에 직접 공개하는 단계가 아니다.
