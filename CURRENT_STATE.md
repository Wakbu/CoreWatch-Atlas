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

공통 메트릭 계약 구현을 완료한 단계다.

- `CoreWatch.Atlas.Contracts`: 비동기 수집 인터페이스와 플랫폼 독립 Snapshot 계약
- 공통 모델: 장비 식별, CPU, 메모리, 파일 시스템, 디스크 누적 I/O, 네트워크 누적 I/O, UTC 수집 시각, 업타임
- 계약 불변 조건: 필수 식별자, CPU 비율 범위, 용량 관계, UTC 시각, 중복 장치 키, 입력 컬렉션 복사
- `CoreWatch.Atlas.Agent`: .NET Worker Service 기본 골격
- `CoreWatch.Atlas.Contracts.Tests`: 정상·경계·오류·취소 계약 테스트 10개
- Windows/Linux Collector, Server/API, Web은 아직 생성하지 않음
- 원격 명령 실행이나 시스템 변경 기능은 구현하지 않음

## 기술 기준

- Target Framework: `net10.0`
- SDK: `10.0.302` (`global.json`, `latestPatch`)
- Runtime/Hosting packages: `10.0.10`
- Test SDK: `MSTest.Sdk/4.3.2`
- Test runner: Microsoft Testing Platform
- 경고는 오류로 처리
- 줄바꿈: 기본 LF, Windows 명령 파일은 CRLF

## 자동 검증

GitHub Actions 파일: `.github/workflows/ci.yml`

- 실행 조건: `main` push, `main` 대상 pull request, 수동 실행
- 운영체제: `ubuntu-latest`, `windows-latest`
- 단계: 복원, Debug 빌드, Release 빌드, 테스트, 취약 패키지, Legacy 패키지 검사
- 최소 `contents: read` 권한
- 최근 `main` 성공 실행: `https://github.com/Wakbu/CoreWatch-Atlas/actions/runs/30242248449`

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
- MTP 테스트: 10/10 통과
- 취약·Legacy 패키지: 없음
- Release Agent 시작 및 테스트 프로세스 정리 통과
- GitHub-hosted Windows와 Ubuntu CI 통과

## 완료된 주요 변경

- PR #1: 초기 .NET 솔루션, Agent, Contracts, 테스트 골격
- PR #2: .NET 10 LTS와 Microsoft Testing Platform 전환
- PR #3: Windows·Ubuntu GitHub Actions CI 추가
- PR #4: 새 채팅 인수인계를 위한 상태·작업 순서 문서 추가
- 공통 메트릭 계약과 불변 조건 테스트 구현

## 알려진 상태와 제한

- Agent는 Worker 템플릿이며 아직 실제 시스템 지표를 수집하지 않는다.
- Server/API와 Web UI는 아직 없다.
- Linux 실장 테스트용 호스트는 아직 연결하지 않았다. GitHub Ubuntu CI는 빌드·테스트 호환성만 검증한다.
- .NET 10 SDK 설치 관리자가 재부팅을 권고했지만 설치 직후 모든 검증은 통과했다.
- 릴리스된 Atlas 버전과 배포 패키지는 아직 없다.

## 다음 작업

다음 구현은 `docs/NEXT_STEPS.md`의 2단계인 Collector 추상화와 Agent 연결이다. 실제 Windows/Linux 수집과 서버 전송은 아직 포함하지 않는다.

## 관련 문서

- 전체 설계: `docs/COREWATCH_ATLAS_DESIGN.md`
- 메트릭 계약: `docs/METRICS_CONTRACT.md`
- 다음 작업: `docs/NEXT_STEPS.md`
- 작업 규칙: `AGENTS.md`
- 제품 소개: `README.md`
