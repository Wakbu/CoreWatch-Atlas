# CoreWatch-Atlas 다음 작업

이 문서는 실행 순서와 각 단계의 완료 조건을 관리한다. 한 번에 한 단계만 승인받아 진행하며, 완료 후 `CURRENT_STATE.md`를 갱신한다.

## 1. 공통 메트릭 계약 (완료)

완료일: 2026-07-27

플랫폼 독립 불변 Snapshot 계약과 자동 테스트를 구현했다.

## 2. Collector 추상화와 Agent 연결 (완료)

완료일: 2026-07-27

Collector DI, 수집 주기, 취소 전달, 오류 격리·재시도와 구조화 로그를 구현했다.

## 3. Linux Collector MVP (완료)

완료일: 2026-07-27

`/proc` 기반 기본 지표와 Ubuntu 실환경 검증을 구현했다.

## 4. Windows Collector MVP (완료)

완료일: 2026-07-27

Windows API 기반 기본 지표와 Windows 실환경 검증을 구현했다.

## 5. Agent 로컬 출력과 Prometheus 형식 (완료)

완료일: 2026-07-27

- camelCase 한 줄 JSON Snapshot 진단 출력
- thread-safe 최신 Snapshot 저장
- 선택적 Kestrel `/metrics`, 기본 loopback·비활성화
- 누적 Counter와 제한된 label 정책
- label escape와 64비트 정수 정밀도
- 수집과 출력 오류 격리

## 6A. Server 기반과 SQLite (완료)

완료일: 2026-07-28

- ASP.NET Core 서버 실행 기반
- SQLite 스키마 v1과 멱등 초기화
- 생존·준비 상태와 서비스 상태 API
- 상태 API·스키마 통합 테스트

## 6B. 장비 등록과 인증

- 영구 Agent ID 발급
- 등록용 일회성 토큰과 Agent 자격 증명
- 자격 증명 해시 저장·폐기·교체
- 등록·인증 API와 실패 감사 로그

## 6C. Snapshot 수신과 조회

- 인증된 Snapshot 수신
- 최신 상태와 SQLite 이력
- 온라인·오프라인 판정
- 보존 기간과 정리 작업

## 7. Web MVP

- 장비 목록과 상태
- 장비별 CPU·메모리·디스크·네트워크 차트
- 기간별 이력과 반응형 UI
- 기존 CoreWatch 개인 사용자판을 참고한 Atlas 전용 웹 디자인 시스템
- 확정된 첫 화면과 내비게이션은 `WEB_DASHBOARD_DESIGN.md`를 기준으로 구현

## 공통 완료 규칙

- 의도하지 않은 파일 변경 확인
- 의존성 복원과 취약·Legacy 패키지 검사
- Debug/Release 경고 0 빌드
- 자동 테스트와 필요한 스모크 테스트
- Windows·Ubuntu GitHub Actions 통과
- 상태·관련 문서 갱신
- 별도 브랜치와 PR, 검증 후 자동 squash 병합·동기화
