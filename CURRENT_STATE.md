# CoreWatch-Atlas 현재 상태

마지막 갱신: 2026-07-27
이 문서는 새로운 채팅이나 작업자가 프로젝트를 즉시 이어받기 위한 기준 문서다. 작업 시작 전에 이 문서, `AGENTS.md`, `docs/NEXT_STEPS.md`를 먼저 읽는다.

## 제품 경계

- 기존 `CoreWatch`: Windows WPF 로컬 진단·벤치마크·최적화 제품
- `CoreWatch-Atlas`: Windows·Linux 장비를 위한 별도 웹 통합 관제 제품
- 저장소: `https://github.com/Wakbu/CoreWatch-Atlas`
- 로컬 경로: `C:\Users\최준용\Documents\CoreWatch\CoreWatch-Atlas`
- 라이선스: Apache License 2.0
- 두 제품은 코드, README, 버전, 태그, CI와 Release를 공유하지 않는다.

## 현재 구현

공통 계약, Agent 수집 오케스트레이션과 Windows·Linux Collector MVP를 완료했다.

- `CoreWatch.Atlas.Contracts`: 비동기 수집 인터페이스와 플랫폼 독립 Snapshot 계약
- `CoreWatch.Atlas.Agent`: 기본 15초 주기 수집, OS별 Collector 자동 선택, 취소 전달, 예외 격리·재시도, 구조화 로그
- `CoreWatch.Atlas.Collectors.Linux`: `/proc` 기반 CPU, 메모리, 파일 시스템, 디스크·네트워크 누적 I/O, 업타임과 장비 식별
- `CoreWatch.Atlas.Collectors.Windows`: Windows API 기반 CPU, 메모리, 고정 볼륨, 물리 디스크·네트워크 누적 I/O와 업타임
- 필수 지표 오류는 전달하고 선택 장치의 권한·접근 오류는 안전하게 격리
- 자동 테스트 30개: 계약 10, Agent 4, Linux 10, Windows 6
- Ubuntu는 실제 `/proc`, Windows는 실제 시스템 API Snapshot을 CI에서 검증
- Server/API, Web과 로컬 JSON·Prometheus 출력은 아직 생성하지 않음
- 원격 명령 실행이나 시스템 변경 기능은 구현하지 않음

## 기술 기준

- Target Framework: `net10.0`
- SDK: `10.0.302` (`global.json`, `latestPatch`)
- Runtime/Hosting packages: `10.0.10`
- Test SDK: `MSTest.Sdk/4.3.2`, Microsoft Testing Platform
- 경고는 오류로 처리
- 줄바꿈: 기본 LF, Windows 명령 파일은 CRLF

## 자동 검증

GitHub Actions `.github/workflows/ci.yml`은 `main` push와 PR에서 Ubuntu·Windows 복원, Debug/Release 빌드, 테스트, 취약·Legacy 패키지 검사를 수행한다.

로컬 검증 명령:

```shell
dotnet restore CoreWatch.Atlas.sln --force-evaluate
dotnet build CoreWatch.Atlas.sln -c Debug --no-restore -warnaserror
dotnet build CoreWatch.Atlas.sln -c Release --no-restore -warnaserror
dotnet test CoreWatch.Atlas.sln -c Release --no-build --no-restore
dotnet list CoreWatch.Atlas.sln package --vulnerable --include-transitive
dotnet list CoreWatch.Atlas.sln package --deprecated --include-transitive
```

마지막 검증 결과:

- Windows 로컬 Debug/Release: 경고 0, 오류 0
- MTP 테스트: 30/30 통과
- 취약·Legacy 패키지: 없음
- Windows Release Agent가 실제 Snapshot을 연속 수집하며 정상 실행
- GitHub-hosted Windows·Ubuntu CI 통과, 각 OS의 실제 Collector 통합 검증

## 완료된 주요 변경

- PR #1~#4: 초기 솔루션, .NET 10, Windows·Ubuntu CI, 인수인계 문서
- PR #5: 공통 메트릭 계약과 불변 조건 테스트
- PR #6: Collector DI와 Agent 주기 수집·취소·예외 복구·구조화 로그
- PR #7: Linux Collector, Agent 연결, fixture와 Ubuntu 실환경 테스트
- Windows Collector MVP: Win32·.NET 시스템 API 수집, Agent 연결, fixture와 Windows 실환경 테스트

## 제품·UI 결정

Atlas Web은 기존 CoreWatch 개인 사용자판의 정보 구성, 색상 감각과 사용 흐름을 참고한다. WPF를 복사하지 않고 반응형 Atlas 웹 디자인 시스템으로 분리하며 기존 CoreWatch에 런타임 의존하지 않는다.

## 알려진 상태와 제한

- Windows·Linux Agent는 실제 Snapshot을 수집하지만 아직 JSON 출력·Prometheus endpoint·서버 전송 기능이 없어 로그에는 수집 성공 이벤트만 남는다.
- Windows 장비 ID는 현재 호스트명 기반이며 서버 등록 단계에서 영구 ID 발급 방식으로 교체할 수 있다.
- 권한이 없거나 성능 카운터를 제공하지 않는 물리 디스크는 Windows 디스크 I/O 컬렉션에서 제외될 수 있다.
- Server/API와 Web UI, 릴리스·배포 패키지는 아직 없다.

## 다음 작업

다음 구현 단계는 `docs/NEXT_STEPS.md`의 5단계인 Agent 로컬 JSON 출력과 선택적 Prometheus `/metrics`다. 시작 전 범위와 완료 조건을 설명하고 사용자 승인을 받는다.

## 관련 문서

- 전체 설계: `docs/COREWATCH_ATLAS_DESIGN.md`
- 메트릭 계약: `docs/METRICS_CONTRACT.md`
- Agent 수집 루프: `docs/AGENT_COLLECTION.md`
- Linux Collector: `docs/LINUX_COLLECTOR.md`
- Windows Collector: `docs/WINDOWS_COLLECTOR.md`
- 다음 작업: `docs/NEXT_STEPS.md`
- 작업 규칙: `AGENTS.md`
