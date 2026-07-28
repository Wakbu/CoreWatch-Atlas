# CoreWatch-Atlas 현재 상태

마지막 갱신: 2026-07-28
새 채팅이나 작업자는 이 문서, `AGENTS.md`, `docs/NEXT_STEPS.md`와 관련 설계를 먼저 읽는다.

## 제품 경계

- 기존 `CoreWatch`: Windows WPF 로컬 진단·벤치마크·최적화 제품
- `CoreWatch-Atlas`: Windows·Linux 장비용 별도 웹 통합 관제 제품
- 저장소: `https://github.com/Wakbu/CoreWatch-Atlas`
- 로컬 경로: `C:\Users\최준용\Documents\CoreWatch\CoreWatch-Atlas`
- 라이선스: Apache License 2.0
- 코드, README, 버전, 태그, CI와 Release를 공유하지 않는다.

## 현재 구현

Windows·Linux Agent가 실제 지표를 수집하고, 중앙 Server가 SQLite 저장소와 상태 API를 제공하는 단계다.

- 플랫폼 독립 불변 Snapshot 계약
- 15초 기본 주기, OS별 Collector 자동 선택, 취소·오류 격리·재시도
- Windows·Linux CPU, 메모리, 파일 시스템, 누적 디스크·네트워크 I/O와 업타임
- thread-safe 최신 Snapshot 저장
- 기본 활성화된 camelCase 한 줄 JSON 표준 출력
- 선택적 Kestrel Prometheus `/metrics`, 기본 `127.0.0.1:9464`·비활성화
- Counter·제한 label·escape·64비트 정수 정밀도 정책
- ASP.NET Core Server, SQLite 스키마 v1과 멱등 초기화
- `/health/live`, `/health/ready`, `/api/v1/status`
- 자동 테스트 41개: 계약 10, Agent 11, Linux 10, Windows 6, Server 4
- 원격 명령 실행이나 시스템 변경 기능 없음

## 기술·검증 기준

- .NET 10 LTS, SDK `10.0.302`, MSTest.Sdk `4.3.2`, Microsoft Testing Platform
- 경고를 오류로 처리, 기본 LF
- CI: Windows·Ubuntu restore, Debug/Release, 테스트, 취약·deprecated 패키지 감사

마지막 검증:

- Windows 로컬 Debug/Release 경고 0·오류 0
- 테스트 41/41 통과
- 취약·deprecated 패키지 없음
- Release Agent JSON 한 줄 출력 확인
- 활성화된 `/metrics` HTTP 200·Prometheus 내용 확인
- Windows·Ubuntu CI와 각 OS 실제 Collector 통합 검증 통과

## 완료된 단계

- PR #1~#4: 솔루션, .NET 10, CI, 인수인계 문서
- PR #5: 공통 메트릭 계약
- PR #6: Agent 수집 오케스트레이션
- PR #7: Linux Collector
- PR #8: Windows Collector
- Agent JSON 출력과 선택적 Prometheus exposition
- Server 기반, SQLite 스키마와 상태 API

## 제품·UI 결정

Atlas Web은 기존 CoreWatch 개인 사용자판의 정보 구성, 색상 감각과 사용 흐름을 참고한다. WPF를 복사하지 않고 반응형 Atlas 웹 디자인 시스템으로 분리하며 기존 CoreWatch에 런타임 의존하지 않는다. 첫 화면은 접이식 왼쪽 기능 목록, 전체 등록 서버 카드, 전체 요약·경고·최근 이벤트·자원 사용 상위 영역으로 구성한다. 서버 카드를 누르면 해당 서버 상세 화면으로 이동한다. 확정 기준은 `docs/WEB_DASHBOARD_DESIGN.md`에 기록했다.

## 알려진 제한

- Server는 기반·상태 API만 있으며 장비 등록·인증과 Snapshot 수신·조회가 없어 통합 조회는 아직 불가능하다.
- Prometheus endpoint에는 아직 인증·TLS가 없으므로 기본 loopback을 유지하거나 사설망·방화벽으로 보호해야 한다.
- Windows 장비 ID는 현재 호스트명 기반이며 서버 등록 단계에서 영구 ID로 교체할 수 있다.
- 정식 Release와 설치·서비스 패키지는 없다.

## 다음 작업

다음 구현은 `docs/NEXT_STEPS.md`의 6B 단계다. 영구 Agent ID, 일회성 등록 토큰과 Agent 자격 증명, 등록·인증 API를 별도 승인 후 진행한다. Snapshot 수신·조회와 온라인 판정은 6C로 분리한다.

## 관련 문서

- 전체 설계: `docs/COREWATCH_ATLAS_DESIGN.md`
- Agent 수집: `docs/AGENT_COLLECTION.md`
- 로컬 출력·Prometheus: `docs/LOCAL_OUTPUT.md`
- Linux Collector: `docs/LINUX_COLLECTOR.md`
- Windows Collector: `docs/WINDOWS_COLLECTOR.md`
- Server 기반: `docs/SERVER_FOUNDATION.md`
- Web 대시보드 설계: `docs/WEB_DASHBOARD_DESIGN.md`
- 다음 작업: `docs/NEXT_STEPS.md`
