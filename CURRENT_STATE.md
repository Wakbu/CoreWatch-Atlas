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

Windows·Linux Agent가 실제 지표를 인증된 중앙 Server로 전송하고, 반응형 Web 대시보드에서 여러 장비의 최신 상태와 최근 24시간 이력을 확인할 수 있는 단계다.

- 플랫폼 독립 불변 Snapshot 계약
- 15초 기본 주기, OS별 Collector 자동 선택, 취소·오류 격리·재시도
- Windows·Linux CPU, 메모리, 파일 시스템, 누적 디스크·네트워크 I/O와 업타임
- thread-safe 최신 Snapshot 저장
- 기본 활성화된 camelCase 한 줄 JSON 표준 출력
- 선택적 Kestrel Prometheus `/metrics`, 기본 `127.0.0.1:9464`·비활성화
- Counter·제한 label·escape·64비트 정수 정밀도 정책
- ASP.NET Core Server, SQLite 스키마 v4와 기존 DB 멱등 업그레이드
- `/health/live`, `/health/ready`, `/api/v1/status`
- 로컬 CLI 일회성 등록 토큰, SHA-256 해시 저장, UUIDv7 영구 Agent ID
- `POST /api/v1/agents/register`, 토큰 만료·1회 소비와 입력 검증
- Agent 자격 증명 해시 저장, Bearer 인증, 교체·폐기와 인증 감사
- Agent Snapshot 전송, 최신·기간별 조회, 온라인 판정과 보존 정리
- 로컬 CLI 운영자 생성, PBKDF2 비밀번호 해시와 로그인 실패 잠금
- `Viewer`·`Administrator` 역할, 쿠키 로그인·로그아웃과 인증 감사
- 대시보드·조회 API 운영자 인증과 관리자 전용 운영자 목록
- Server와 함께 제공되는 반응형 Web 대시보드, 검색·파생 경고·이벤트·상세 차트
- 자동 테스트 60개: 계약 10, Agent 15, Linux 10, Windows 6, Server 19
- 원격 명령 실행이나 시스템 변경 기능 없음

## 기술·검증 기준

- .NET 10 LTS, SDK `10.0.302`, MSTest.Sdk `4.3.2`, Microsoft Testing Platform
- 경고를 오류로 처리, 기본 LF
- CI: Windows·Ubuntu restore, Debug/Release, 테스트, 취약·deprecated 패키지 감사

마지막 검증:

- Windows 로컬 Debug/Release 경고 0·오류 0
- 테스트 60/60 통과
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
- 일회성 등록 토큰과 영구 Agent ID 발급
- Agent 자격 증명과 Server/API MVP
- 반응형 Web 대시보드 MVP
- 운영 보안 8A: 운영자 로그인과 Viewer·Administrator 조회 권한

## 제품·UI 결정

Atlas Web은 기존 CoreWatch 개인 사용자판의 정보 구성, 색상 감각과 사용 흐름을 참고한다. WPF를 복사하지 않고 반응형 Atlas 웹 디자인 시스템으로 분리하며 기존 CoreWatch에 런타임 의존하지 않는다. 첫 화면은 접이식 왼쪽 기능 목록, 전체 등록 서버 카드, 전체 요약·경고·최근 이벤트·자원 사용 상위 영역으로 구성한다. 서버 카드를 누르면 해당 서버 상세 화면으로 이동한다. 확정 기준은 `docs/WEB_DASHBOARD_DESIGN.md`에 기록했다.

## 알려진 제한

- HTTPS가 아직 없으므로 운영 인터넷에 직접 공개하지 않고 loopback 또는 신뢰된 사설망에서 운영해야 한다.
- 운영자 계정 생성은 현재 Server 로컬 CLI만 지원하며 MFA와 계정 수명주기 UI는 없다.
- 운영자 쿠키 Data Protection 키의 운영용 영구·보호 저장은 다음 비밀정보 관리 단계에서 구현한다.
- Prometheus endpoint에는 아직 인증·TLS가 없으므로 기본 loopback을 유지하거나 사설망·방화벽으로 보호해야 한다.
- Agent 등록 정보와 자격 증명은 아직 자동 영구 저장되지 않으므로 운영자가 환경 변수나 별도 비밀 저장소로 주입해야 한다.
- 정식 Release와 설치·서비스 패키지는 없다.

## 다음 작업

다음 구현은 `docs/NEXT_STEPS.md`의 8B다. HTTPS 강제와 Server·Agent 비밀정보 저장을 설계·구현한다.

## 관련 문서

- 전체 설계: `docs/COREWATCH_ATLAS_DESIGN.md`
- Agent 수집: `docs/AGENT_COLLECTION.md`
- 로컬 출력·Prometheus: `docs/LOCAL_OUTPUT.md`
- Linux Collector: `docs/LINUX_COLLECTOR.md`
- Windows Collector: `docs/WINDOWS_COLLECTOR.md`
- Server 기반: `docs/SERVER_FOUNDATION.md`
- Server/API MVP: `docs/SERVER_API_MVP.md`
- Web 대시보드 설계: `docs/WEB_DASHBOARD_DESIGN.md`
- Web 대시보드 MVP: `docs/WEB_DASHBOARD_MVP.md`
- 다음 작업: `docs/NEXT_STEPS.md`
## 2026-07-28 Web UI 정합성 수정

- 인증 추가 후 드러난 데스크톱 Grid 자동 배치 충돌을 수정해 본문이 사이드바 아래로 밀리지 않게 했다.
- 접힌 사이드바의 토글 버튼과 화살표를 중앙 정렬하고 메뉴와 겹치지 않게 했다.
- 기존 CoreWatch 개인 사용자판의 밝은 캔버스, 흰 패널, 짙은 사이드바와 자원별 포인트 색상을 Atlas Web에 반영했다.
- 정적 자산 테스트에 데스크톱 backdrop 숨김과 CoreWatch 배경 토큰 회귀 검사를 추가했다.
