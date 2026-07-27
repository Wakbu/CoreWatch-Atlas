# CoreWatch-Atlas 크로스플랫폼 전환 설계

## 1. 문서 목적

이 문서는 기존 Windows 전용 **CoreWatch**를 변경하지 않고, Windows와 Linux 장비를 웹에서 통합 관제하는 별도 제품 **CoreWatch-Atlas**를 설계하기 위한 기준 문서다.

이 단계에서는 기존 소스 코드, 기존 `README.md`, 기존 빌드 및 배포 구조를 수정하지 않는다. CoreWatch-Atlas 구현은 별도 저장소에서 시작한다.

## 2. 제품 분리 결정

### 기존 제품: CoreWatch

- 제품 성격: 한 대의 Windows PC를 위한 로컬 진단·벤치마크·최적화 앱
- UI: WPF
- 지원 OS: Windows x64
- 저장소: `Wakbu/CoreWatch`
- 버전 계열: 현재 `6.x`
- 배포물: Windows ZIP 및 설치 프로그램
- 유지 원칙: 현재 기능과 릴리스 정책을 그대로 유지

### 신규 제품: CoreWatch-Atlas

- 제품 성격: 여러 Windows·Linux 장비를 웹에서 통합 모니터링하는 관제 플랫폼
- UI: 웹 대시보드
- 지원 OS: Windows x64, Linux x64를 시작점으로 함
- 제안 저장소: `Wakbu/CoreWatch-Atlas`
- 버전 계열: `0.1.0`부터 독립적으로 시작
- 배포물: 서버, 에이전트, 컨테이너 이미지 및 설치 패키지

`Atlas`는 여러 장비의 상태를 한곳에 모아 전체 환경을 보여 준다는 의미다. 기존 CoreWatch와 이름의 연속성을 유지하면서도 별도 제품임을 구분할 수 있다.

## 3. 저장소 및 Git 운영

CoreWatch와 CoreWatch-Atlas는 브랜치가 아닌 **별도 Git 저장소**로 관리한다.

브랜치로 분리하면 다음 항목이 계속 섞인다.

- GitHub 메인 `README`
- Issues, Pull Requests, Actions
- 태그와 Release 목록
- 버전 번호와 변경 이력
- 배포 파일 및 보안 정책

따라서 저장소를 다음과 같이 분리한다.

| 구분 | 기존 제품 | 신규 제품 |
|---|---|---|
| 저장소 | `Wakbu/CoreWatch` | `Wakbu/CoreWatch-Atlas` |
| 기본 브랜치 | `main` | `main` |
| 제품 버전 | `6.x` | `0.x` → `1.x` |
| 태그 | `v6.1.1` 등 | `v0.1.0` 등 |
| Release | Windows 앱 | Atlas 서버·에이전트 |
| README | 로컬 Windows 진단 | 크로스플랫폼 통합 관제 |

### 저장소 간 코드 정책

- 기존 CoreWatch 저장소를 복제하여 Atlas를 시작하지 않는다.
- Windows 전용 코드 전체를 신규 저장소에 복사하지 않는다.
- 재사용 가치가 있는 모델이나 계산 로직만 검토 후 신규 구조에 맞게 이식한다.
- 두 저장소 사이에 Git submodule이나 파일 자동 동기화를 초기부터 도입하지 않는다.
- 공통 패키지가 실제로 필요해질 때만 별도 패키지 저장소 또는 NuGet 패키지를 검토한다.

이 정책은 두 제품이 서로의 빌드와 배포를 막지 않도록 하기 위한 것이다.

## 4. README 관리 원칙

### `Wakbu/CoreWatch/README.md`

기존 README는 다음 내용만 다룬다.

- Windows 로컬 앱 소개
- WPF 화면 및 로컬 기능
- Windows 설치 요구사항
- 최신 CoreWatch 다운로드
- 기존 안전·개인정보 정책

문서 하단에 다음 정도의 짧은 안내만 추가할 수 있다.

> 여러 Windows·Linux 장비의 웹 통합 관제가 필요하면 별도 프로젝트 CoreWatch-Atlas를 참고하세요.

### `Wakbu/CoreWatch-Atlas/README.md`

신규 README는 다음 내용만 다룬다.

- Atlas의 목적과 지원 운영체제
- 서버·에이전트·웹 구조
- 빠른 시작 방법
- Docker 및 서비스 설치 방법
- 장비 등록과 인증 방식
- Prometheus 연동 방법
- Atlas 버전과 다운로드

Atlas README에서 기존 CoreWatch는 다음 정도로 구분한다.

> 단일 Windows PC의 로컬 진단·벤치마크·최적화 기능은 CoreWatch를 사용하세요.

각 README의 다운로드 링크, 버전, 해시 및 변경 이력은 해당 저장소의 제품만 가리켜야 한다.

## 5. 목표와 비목표

### 1차 목표

- Windows와 Linux의 공통 시스템 지표 수집
- 여러 장비 등록 및 온라인 상태 확인
- 웹 기반 실시간 현황과 기간별 이력 조회
- Prometheus 호환 메트릭 제공
- 컨테이너 또는 일반 실행 파일을 통한 서버 배포
- 에이전트와 서버 사이의 인증 및 암호화

### 초기 비목표

- 기존 CoreWatch의 모든 최적화 기능 이식
- 원격 프로세스 종료 및 원격 명령 실행
- Windows WPF UI 대체
- macOS 정식 지원
- 모든 GPU·메인보드 센서 지원
- Prometheus 자체를 완전히 대체

원격 제어 기능은 보안 위험이 크므로 모니터링 기능이 안정화된 후 별도 설계한다.

## 6. 전체 구조

```text
Windows Host ─ CoreWatch Atlas Agent ─┐
Linux Host  ─ CoreWatch Atlas Agent ──┼─ Atlas Server/API ─ Atlas Web
Prometheus  ─ 선택적 연동 ────────────┘
```

### 구성 요소

#### CoreWatch.Atlas.Contracts

- 장비, 메트릭, 상태, 경고에 대한 공통 계약
- API 요청·응답 DTO
- 플랫폼에 독립적인 단위와 명칭 정의

#### CoreWatch.Atlas.Agent

- 각 장비에서 백그라운드 서비스로 실행
- 운영체제별 Collector 호출
- 서버에 상태 및 지표 전송
- Prometheus 형식의 `/metrics` 선택 제공
- 로컬 설정과 인증 키 보관

#### CoreWatch.Atlas.Collectors.Windows

- Windows CPU, 메모리, 디스크, 네트워크, 업타임 수집
- 필요한 범위에서 PDH, Win32 API 또는 WMI 사용
- 기존 CoreWatch 코드를 직접 참조하지 않고 독립 구현

#### CoreWatch.Atlas.Collectors.Linux

- `/proc/stat`: CPU
- `/proc/meminfo`: 메모리
- `/proc/diskstats`: 디스크 I/O
- `/proc/net/dev`: 네트워크
- `/proc/uptime`: 업타임
- `/sys/class/hwmon`: 사용 가능한 온도 센서

#### CoreWatch.Atlas.Server

- 장비 등록, 인증 및 상태 관리
- 메트릭 수신 및 조회 API
- 경고 판정
- 데이터 보존 정책 실행
- 웹 UI 제공 또는 별도 Web 프로젝트 호스팅

#### CoreWatch.Atlas.Web

- 장비 목록 및 온라인 상태
- CPU·메모리·디스크·네트워크 차트
- 장비별 상세 화면
- 기간별 이력 및 경고 조회
- 반응형 브라우저 UI

## 7. 권장 기술

| 영역 | 권장 기술 |
|---|---|
| 공통 런타임 | .NET 10 LTS |
| 에이전트 | .NET Worker Service |
| 서버/API | ASP.NET Core |
| 웹 UI | Blazor Web App |
| 실시간 갱신 | SignalR 또는 일정 주기 API 조회 |
| 초기 저장소 | SQLite |
| 운영 저장소 | PostgreSQL |
| 메트릭 호환 | Prometheus exposition format |
| 컨테이너 | Docker/OCI |
| 테스트 | xUnit 및 통합 테스트 |

Blazor를 우선 제안하는 이유는 기존 C# 자산과 개발 경험을 활용하고, 서버·UI 간 모델 공유를 쉽게 하기 위해서다. 대규모 프론트엔드 요구가 생기면 React 등으로 교체할 수 있다.

## 8. 공통 수집 계약

운영체제별 구현을 분리하기 위해 다음과 같은 개념의 계약을 둔다.

```csharp
public interface ISystemMetricsCollector
{
    string Platform { get; }
    ValueTask<SystemMetricsSnapshot> CaptureAsync(
        CancellationToken cancellationToken);
}
```

첫 번째 공통 Snapshot은 다음 값으로 제한한다.

- 수집 시각
- CPU 사용률
- 전체·사용·가용 메모리
- 파일 시스템별 전체·가용 공간
- 디스크 읽기·쓰기 누적 바이트
- 인터페이스별 송수신 누적 바이트
- 업타임
- 에이전트 버전

네트워크와 디스크는 가능하면 초당 속도가 아닌 누적값을 저장한다. 속도는 두 수집 시점의 차이로 계산하여 재시작이나 수집 간격 변화에 대응한다.

## 9. Prometheus 호환 정책

Atlas Agent는 선택적으로 `/metrics`를 제공한다.

```text
corewatch_atlas_cpu_usage_ratio 0.237
corewatch_atlas_memory_total_bytes 17179869184
corewatch_atlas_memory_available_bytes 8766910464
corewatch_atlas_filesystem_available_bytes{mount="/"} 120034123776
corewatch_atlas_network_receive_bytes_total{device="eth0"} 98234122
corewatch_atlas_agent_info{version="0.1.0",os="linux"} 1
```

정책은 다음과 같다.

- 단위는 metric 이름에 표시한다.
- 누적값에는 `_total`을 사용한다.
- 장비 ID, 마운트, 네트워크 장치 등 제한된 label만 허용한다.
- PID처럼 빠르게 변하는 값을 label로 사용하지 않는다.
- 전체 프로세스별 메트릭은 기본적으로 수집하지 않는다.
- Prometheus 없이도 Atlas 자체 기능이 동작해야 한다.

## 10. 데이터 흐름

초기 구현은 Agent가 Server로 전송하는 Push 방식을 기본으로 한다.

1. 관리자가 Atlas Server에서 등록 토큰을 발급한다.
2. Agent 설치 시 서버 주소와 등록 토큰을 입력한다.
3. 서버가 장비별 자격 증명을 발급한다.
4. Agent가 일정 주기로 공통 Snapshot을 HTTPS 전송한다.
5. Server가 최신 상태와 이력을 저장한다.
6. Web이 Server API를 통해 조회한다.

Prometheus를 사용하는 환경에서는 Prometheus가 Agent의 `/metrics`를 Pull할 수 있다. Atlas 자체 전송과 Prometheus 연동은 서로 독립적으로 활성화한다.

## 11. 보안 기준

- 모든 원격 통신은 HTTPS를 기본으로 한다.
- 최초 등록 토큰은 짧은 유효기간과 일회성 사용을 지원한다.
- 장비마다 별도 자격 증명을 발급한다.
- 서버 주소와 인증서 검증을 끌 수 없도록 기본값을 정한다.
- 로그에 토큰, 인증 헤더 및 민감한 환경 변수를 기록하지 않는다.
- Agent API는 기본적으로 외부 명령 실행 기능을 제공하지 않는다.
- `/metrics` 공개 범위를 로컬, 사설망 또는 인증 사용으로 선택할 수 있게 한다.
- 서버 관리자 계정과 장비 인증을 분리한다.

## 12. 저장 및 보존

### 개발·단일 사용자

- SQLite
- 기본 30일 보존
- 오래된 원시 샘플 자동 삭제

### 다중 장비 운영

- PostgreSQL
- 최근 데이터와 장기 집계 데이터를 분리
- 장비 수와 수집 주기에 맞춘 보존 정책 제공

처음부터 모든 초 단위 데이터를 영구 저장하지 않는다. 예를 들어 원시 샘플은 30일, 시간 단위 집계는 1년처럼 단계별 보존을 추후 적용한다.

## 13. 저장소 초안

```text
CoreWatch-Atlas/
├─ README.md
├─ LICENSE
├─ AGENTS.md
├─ CHANGELOG.md
├─ Directory.Build.props
├─ CoreWatch.Atlas.sln
├─ docs/
│  ├─ architecture.md
│  ├─ security.md
│  └─ deployment.md
├─ src/
│  ├─ CoreWatch.Atlas.Contracts/
│  ├─ CoreWatch.Atlas.Agent/
│  ├─ CoreWatch.Atlas.Collectors.Windows/
│  ├─ CoreWatch.Atlas.Collectors.Linux/
│  ├─ CoreWatch.Atlas.Server/
│  └─ CoreWatch.Atlas.Web/
├─ tests/
│  ├─ CoreWatch.Atlas.Contracts.Tests/
│  ├─ CoreWatch.Atlas.Collectors.Tests/
│  └─ CoreWatch.Atlas.Server.Tests/
└─ deploy/
   ├─ docker/
   ├─ systemd/
   └─ windows-service/
```

## 14. 버전 및 릴리스 정책

### 버전

- `0.1.0`: Agent가 로컬 공통 지표를 수집하고 출력
- `0.2.0`: Server 등록·수신·저장
- `0.3.0`: Web 장비 목록 및 기본 차트
- `0.4.0`: 인증, TLS, 설치 서비스
- `0.5.0`: Prometheus 호환 및 Docker 배포
- `1.0.0`: Windows/Linux 기본 관제 기능과 운영 검증 완료

CoreWatch 버전과 Atlas 버전은 서로 연동하지 않는다. 예를 들어 CoreWatch `6.2.0`과 CoreWatch-Atlas `0.3.0`은 독립적으로 존재할 수 있다.

### 릴리스 파일명

```text
CoreWatch-Atlas-Agent-v0.1.0-win-x64.zip
CoreWatch-Atlas-Agent-v0.1.0-linux-x64.tar.gz
CoreWatch-Atlas-Server-v0.1.0-linux-x64.tar.gz
CoreWatch-Atlas-Server-v0.1.0-win-x64.zip
```

컨테이너 이미지는 다음 형식을 사용한다.

```text
ghcr.io/wakbu/corewatch-atlas-server:0.1.0
ghcr.io/wakbu/corewatch-atlas-server:latest
```

각 GitHub Release에는 다음을 제공한다.

- 운영체제별 실행 패키지
- SHA-256 체크섬 목록
- 변경 사항
- 업그레이드 및 호환성 주의사항
- 지원되는 서버·에이전트 버전 범위

## 15. CI/CD 분리

각 저장소는 자체 GitHub Actions를 사용한다.

### CoreWatch

- 기존 Windows Debug/Release 빌드
- WPF 및 설치 프로그램 검증
- 기존 ZIP과 Setup 릴리스

### CoreWatch-Atlas

- Windows와 Linux 교차 빌드
- 단위·통합 테스트
- Linux 컨테이너 테스트
- Agent와 Server 스모크 테스트
- 패키지 내부 파일·크기·SHA-256 검증
- 태그 기반 Atlas 전용 GitHub Release

한 제품의 CI 실패가 다른 제품의 배포를 차단하지 않아야 한다.

## 16. 단계별 구현 계획

### 단계 0: 저장소 준비

- `Wakbu/CoreWatch-Atlas` 생성
- Atlas 전용 README, 라이선스, 작업 규칙 작성
- 솔루션과 공통 빌드 정책 구성

완료 기준: Windows와 Linux CI에서 빈 솔루션의 Debug/Release 빌드가 성공한다.

### 단계 1: 로컬 Agent MVP

- 공통 계약 작성
- Windows/Linux 기본 Collector 구현
- 콘솔 또는 로그로 Snapshot 확인
- Collector 단위 테스트 작성

완료 기준: 두 운영체제에서 CPU·메모리·디스크·네트워크·업타임이 정상 범위로 수집된다.

### 단계 2: Server MVP

- 장비 등록
- Snapshot 수신
- 최신 상태와 SQLite 이력 저장
- 상태 조회 API

완료 기준: 두 개 이상의 테스트 Agent가 독립 장비로 표시된다.

### 단계 3: Web MVP

- 장비 목록
- 온라인·오프라인 상태
- 장비 상세 및 기본 차트
- 기간 선택

완료 기준: 브라우저에서 Windows와 Linux 장비 이력을 함께 확인할 수 있다.

### 단계 4: 운영 준비

- HTTPS와 장비별 인증
- systemd 및 Windows Service 설치
- Docker 배포
- PostgreSQL 지원
- 데이터 보존 및 백업
- Prometheus `/metrics`

완료 기준: 재부팅, 네트워크 단절, 서버 재시작 후에도 자동 복구된다.

### 단계 5: 1.0 안정화

- 장기 부하 및 저장 용량 검증
- 업그레이드 호환성 검증
- 보안 검토
- 설치·제거·복구 문서
- 정식 릴리스 패키지 검증

## 17. 기존 CoreWatch와의 관계

CoreWatch는 Atlas Agent의 필수 구성 요소가 아니다. 두 프로그램은 각각 단독 실행할 수 있어야 한다.

향후 선택 기능으로 다음 연동만 검토한다.

- CoreWatch에서 “Atlas에 이 장비 등록” 안내
- CoreWatch의 로컬 진단 결과 중 사용자가 선택한 항목만 Atlas로 전송
- Atlas 웹에서 해당 Windows 장비의 CoreWatch 설치 여부 표시

기존 CoreWatch가 Atlas Server에 종속되거나, Atlas 설치를 강제해서는 안 된다.

## 18. 다음 작업의 범위

다음 구현 작업은 기존 CoreWatch 저장소에서 진행하지 않는다. 먼저 별도 `CoreWatch-Atlas` 저장소 또는 별도 작업 폴더를 준비한 후 아래 항목만 수행한다.

1. 신규 저장소 골격 생성
2. Atlas 전용 `README.md` 작성
3. 공통 계약 프로젝트 생성
4. Windows/Linux에서 빌드되는 최소 Agent 생성
5. 코드·의존성·Debug/Release·스모크 테스트 검증

Server와 Web 구현은 Agent MVP 검증이 끝난 뒤 별도 단계로 진행한다.

## 19. 최종 결정 요약

- 기존 **CoreWatch**는 Windows WPF 로컬 관리 제품으로 유지한다.
- 신규 **CoreWatch-Atlas**는 크로스플랫폼 웹 통합 관제 제품으로 분리한다.
- 두 제품은 별도 GitHub 저장소, README, 버전, 태그, CI 및 Release를 사용한다.
- Atlas는 Agent, 운영체제별 Collector, Server/API, Web으로 구성한다.
- 초기 범위는 읽기 전용 기본 모니터링이며 원격 제어는 포함하지 않는다.
- 기존 코드는 이번 설계 단계에서 변경하지 않는다.
## 20. Web 디자인 기준

Atlas Web은 기존 CoreWatch 개인 사용자판의 정보 우선순위, 색상 감각과 익숙한 사용 흐름을 참고한다. WPF 화면을 그대로 복제하지 않고 데스크톱·태블릿·모바일 브라우저에서 동작하는 반응형 레이아웃과 Atlas 전용 컴포넌트·색상 토큰으로 재설계한다. 실제 Web 단계에서는 기존 화면을 먼저 분석하되 기존 CoreWatch 소스나 리소스에 런타임 의존성을 만들지 않는다.
