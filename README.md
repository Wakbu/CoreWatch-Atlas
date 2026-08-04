# CoreWatch-Atlas

CoreWatch-Atlas는 Windows와 Linux 장비의 상태를 한곳에서 확인하기 위한 크로스플랫폼 시스템 모니터링 플랫폼입니다.

> 여러 서버의 상태를 Web 대시보드에서 확인하고, 경고·그룹·자산·점검 이력을 운영할 수 있는 Agent 기반 관제 도구입니다.

## 설치와 배포

- **Server 설치**: `corewatch-atlas-server.zip`을 배포 서버에 설치하고 HTTPS 주소로 Web 대시보드에 접속합니다.
- **Agent 등록**: 대시보드의 `Agent 등록`에서 대상 OS를 선택하고 1회용 설치 명령을 생성합니다. 해당 명령을 대상 Windows 또는 Linux 서버에서 관리자 권한으로 실행합니다.
- **상태 확인**: 등록된 서버는 대시보드와 서버 목록에서 CPU·메모리·디스크·네트워크·마지막 수집 시각을 확인할 수 있습니다.
- **운영 관리**: 서버 그룹, 자산 정보, 경고 규칙, 유지보수 시간, 진단 설정과 PDF/CSV 보고서를 Web에서 관리합니다.
- 배포 ZIP과 SHA-256 파일은 [GitHub Releases](https://github.com/Wakbu/CoreWatch-Atlas/releases)에서 제공하며, 상세 절차는 [운영 배포 문서](docs/SECURITY_DEPLOYMENT.md)를 참고하세요.

## 현재 기능

- Windows·Linux CPU, 메모리, 파일 시스템, 디스크·네트워크 누적 I/O와 업타임 수집
- camelCase 한 줄 JSON Snapshot 출력
- 선택적 Prometheus 호환 `/metrics`
- 읽기 전용 동작과 수집·출력 오류 격리
- ASP.NET Core 중앙 서버, SQLite 스키마와 상태 API
- 일회성 등록 토큰과 UUIDv7 영구 Agent ID 발급
- Agent 자격 증명, Snapshot 수신·조회, 온라인 판정과 보존 정리
- Viewer·Administrator 운영자 로그인과 역할 기반 조회 권한
- HTTPS 강제, CSRF·보안 헤더와 영구 Data Protection 키
- Agent 자격 증명 안전 저장과 등록·교체 CLI
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

첫 운영자는 로컬 CLI에서 만든다. 기본 역할은 `Administrator`다.

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release -- --create-operator admin
```

Agent 인증, Snapshot 전송·조회와 Web 대시보드를 함께 제공합니다. 실행 후 Server 루트 URL을 브라우저로 열면 됩니다. 설정과 API는 [Server/API MVP 문서](docs/SERVER_API_MVP.md), HTTPS·비밀 저장은 [보안 운영 문서](docs/SECURITY_DEPLOYMENT.md), 화면 기능은 [Web 대시보드 MVP 문서](docs/WEB_DASHBOARD_MVP.md)를 참고하세요.

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
5. 운영자 인증·조회 권한: 완료
6. HTTPS와 비밀정보 관리: 완료
7. 경고, 서비스 설치와 Docker 배포: 완료
8. 운영 배포 패키지 제공: 완료


## 개발 빌드

```shell
dotnet restore CoreWatch.Atlas.sln
dotnet build CoreWatch.Atlas.sln -c Debug --no-restore
dotnet build CoreWatch.Atlas.sln -c Release --no-restore
dotnet test CoreWatch.Atlas.sln -c Release --no-build
dotnet list CoreWatch.Atlas.sln package --vulnerable --include-transitive
```

## 배포와 라이선스

Atlas는 기존 CoreWatch와 독립적으로 관리됩니다. 최신 설치 패키지와 변경 사항은 [GitHub Releases](https://github.com/Wakbu/CoreWatch-Atlas/releases)에서 확인할 수 있습니다. 라이선스는 [Apache License 2.0](LICENSE)입니다.
