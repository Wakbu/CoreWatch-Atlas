# CoreWatch-Atlas Server 기반

## 범위

Server/API 기반은 중앙 서버, SQLite 저장소, 일회성 등록, Agent 자격 증명과 Snapshot 수신·조회를 제공한다. 상세 API와 운영 설정은 `SERVER_API_MVP.md`에서 관리한다.

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

운영자 계정도 Server 로컬 CLI에서 생성한다. 공개 회원가입 API는 없다.

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release -- --create-operator admin
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release -- --create-operator observer --role Viewer
```

역할 기본값은 `Administrator`이며 `Viewer`를 선택할 수 있다. 사용자 이름은 영문·숫자와 `._-`로 구성한 3~64자, 비밀번호는 12~128자다. 비밀번호는 화면에 표시하지 않는 대화형 입력으로 두 번 확인하며 DB에는 ASP.NET Core PasswordHasher의 PBKDF2 해시만 저장한다.

로그인은 기본 30분 sliding session 쿠키를 사용한다. 5회 연속 실패하면 15분 잠그며 세 값은 `Atlas:OperatorAuthentication`에서 허용 범위 안으로 조정할 수 있다.

## 상태·등록 API

| 경로 | 용도 |
|---|---|
| `GET /health/live` | 프로세스 생존 확인 |
| `GET /health/ready` | SQLite 연결과 현재 스키마 확인 |
| `GET /api/v1/status` | 서비스 버전·시각·저장소 상태 확인 |
| `POST /api/v1/agents/register` | 유효한 일회성 토큰을 소비하고 UUIDv7 Agent ID 발급 |

시작 시 데이터베이스 초기화에 실패하면 서버도 즉시 실패한다. 준비되지 않은 서버가 요청을 받지 않게 하는 fail-fast 정책이다.

## SQLite 스키마 v4

- `schema_migrations`: 적용된 스키마 버전
- `agents`: 영구 Agent ID와 등록 당시 장비 정보
- `snapshots`: Agent별 원본 JSON Snapshot 이력
- `registration_tokens`: 토큰 해시, 만료·소비 시각과 발급된 Agent ID
- `agents` 자격 증명 열: 자격 증명 해시, 생성·폐기 시각
- `authentication_audit`: 인증 실패·교체·폐기 감사 이벤트
- `atlas_operators`: 운영자 이름, 비밀번호 해시, 역할과 잠금 상태
- `operator_authentication_audit`: 로그인 성공·실패·잠금과 로그아웃
- `ix_snapshots_agent_captured_at`: 장비·수집 시각 기준 이력 조회

외래 키, WAL 저널과 5초 busy timeout을 적용한다. 마이그레이션과 등록 처리는 트랜잭션으로 중복 실행과 토큰 재사용을 방지한다.

## 현재 보안 경계

상태 API에는 비밀값이나 데이터베이스 경로를 노출하지 않는다. 등록 토큰과 운영자 생성은 HTTP API로 노출하지 않고 로컬 CLI에서만 수행하며 Agent 자격 증명 원문도 최초 응답에서만 반환한다. 대시보드와 조회 API에는 운영자 인증이 적용됐다. 아직 HTTPS와 운영용 Data Protection key 저장이 없으므로 운영 인터넷에 직접 공개하는 단계는 아니다.
