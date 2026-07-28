# CoreWatch-Atlas

CoreWatch-Atlas는 Windows와 Linux 장비의 상태를 한곳에서 확인하기 위한 크로스플랫폼 시스템 모니터링 플랫폼입니다.

> 현재 Agent 수집·중앙 전송, Server/API와 Web 대시보드 MVP까지 구현됐으며 정식 배포 패키지는 아직 없습니다.

## 현재 기능

- Windows·Linux CPU, 메모리, 파일 시스템, 디스크·네트워크 누적 I/O와 업타임 수집
- camelCase 한 줄 JSON Snapshot 출력
- 선택적 Prometheus 호환 `/metrics`
- 읽기 전용 동작과 수집·출력 오류 격리
- ASP.NET Core 중앙 서버, SQLite 스키마와 상태 API
- 일회성 등록 토큰과 UUIDv7 영구 Agent ID 발급
- Agent 자격 증명, Snapshot 수신·조회, 온라인 판정과 보존 정리
- 반응형 Web 대시보드, 서버 카드·검색·경고·이력 차트

[현재 구현 및 인수인계](CURRENT_STATE.md) · [다음 작업](docs/NEXT_STEPS.md) · [전체 설계](docs/COREWATCH_ATLAS_DESIGN.md)

## 개발 요구사항

- .NET SDK 10.0.302 이상
- Windows 또는 Linux

## 로컬 실행

```shell
dotnet run --project src/CoreWatch.Atlas.Agent/CoreWatch.Atlas.Agent.csproj -c Release
```

기본 설정은 15초마다 JSON Snapshot을 출력하며 Prometheus endpoint는 비활성화돼 있습니다. `/metrics` 활성화와 지표 목록은 [로컬 출력 문서](docs/LOCAL_OUTPUT.md)를 참고하세요.

중앙 서버 기반은 다음 명령으로 실행합니다.

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release
```

Agent 인증, Snapshot 전송·조회와 Web 대시보드를 함께 제공합니다. 실행 후 Server 루트 URL을 브라우저로 열면 됩니다. 설정과 API는 [Server/API MVP 문서](docs/SERVER_API_MVP.md), 화면 기능은 [Web 대시보드 MVP 문서](docs/WEB_DASHBOARD_MVP.md)를 참고하세요.

## 제품 구분

- **CoreWatch**: 단일 Windows PC를 위한 WPF 로컬 진단·벤치마크·최적화 앱
- **CoreWatch-Atlas**: 여러 Windows·Linux 장비를 위한 Agent·Server·Web 통합 관제 플랫폼

두 제품은 저장소, 버전, 태그, CI/CD와 GitHub Release를 독립적으로 관리합니다.

## 목표 구조

```text
Windows Host ─ CoreWatch Atlas Agent ─┐
Linux Host  ─ CoreWatch Atlas Agent ──┼─ Atlas Server/API ─ Atlas Web
Prometheus  ─ 선택적 연동 ────────────┘
```

초기 버전은 읽기 전용 모니터링에 집중하며 원격 명령 실행과 시스템 최적화를 포함하지 않습니다.

## 개발 상태

1. 저장소와 공통 빌드 구조: 완료
2. Windows/Linux Agent와 로컬 출력: 완료
3. 중앙 Server/API MVP와 Agent 전송: 완료
4. 웹 대시보드 MVP: 완료
5. 운영자 인증, 서비스 설치와 Docker 배포
6. `1.0.0` 안정화

## 개발 빌드

```shell
dotnet restore CoreWatch.Atlas.sln
dotnet build CoreWatch.Atlas.sln -c Debug --no-restore
dotnet build CoreWatch.Atlas.sln -c Release --no-restore
dotnet test CoreWatch.Atlas.sln -c Release --no-build
dotnet list CoreWatch.Atlas.sln package --vulnerable --include-transitive
```

## 버전과 라이선스

Atlas는 기존 CoreWatch와 독립된 `0.x` 버전으로 시작하며 정식 운영 요구사항을 충족한 뒤 `1.0.0`을 배포합니다. 라이선스는 [Apache License 2.0](LICENSE)입니다.
