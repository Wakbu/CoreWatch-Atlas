# CoreWatch-Atlas 다음 작업

이 문서는 실행 순서와 각 단계의 완료 조건을 관리한다. 한 번에 한 단계만 승인받아 진행하며, 완료 후 `CURRENT_STATE.md`를 갱신한다.

## 1. 공통 메트릭 계약 (완료)

완료일: 2026-07-27

Windows와 Linux Collector가 공유하는 불변 Snapshot 계약과 자동 테스트 10개를 구현했다.

## 2. Collector 추상화와 Agent 연결 (완료)

완료일: 2026-07-27

Collector DI, 설정 가능한 수집 주기, 취소 전달, 오류 격리·재시도와 구조화 로그를 구현했다.

## 3. Linux Collector MVP (완료)

완료일: 2026-07-27

- `/proc` 기반 CPU, 메모리, 디스크·네트워크 누적 I/O와 업타임
- 접근 가능한 파일 시스템과 Linux 장비 식별
- fixture 및 Ubuntu 실제 `/proc` 통합 테스트

## 4. Windows Collector MVP (완료)

완료일: 2026-07-27

- Windows API 기반 CPU, 메모리와 업타임
- 고정 볼륨 용량, 물리 디스크·네트워크 누적 I/O
- Windows 종속 코드를 독립 Collector 프로젝트로 격리
- 기존 CoreWatch 코드 직접 참조 없음
- fixture 및 Windows 실제 API 통합 테스트
- Windows Agent의 실제 Collector 자동 선택

## 5. Agent 로컬 출력과 Prometheus 형식

- JSON Snapshot 진단 출력
- 선택적 `/metrics`
- 누적 Counter와 제한된 label 정책
- 프로세스별 고카디널리티 지표 제외

## 6. Server/API MVP

- 장비 등록과 인증
- Snapshot 수신
- 최신 상태와 SQLite 이력
- 온라인·오프라인 판정

## 7. Web MVP

- 장비 목록과 상태
- 장비별 CPU·메모리·디스크·네트워크 차트
- 기간별 이력과 반응형 UI
- 기존 CoreWatch 개인 사용자판의 시각 언어와 사용 흐름을 참고하되 Atlas 전용 웹 디자인 시스템으로 분리

## 공통 완료 규칙

- 의도하지 않은 파일 변경 확인
- 의존성 복원과 취약·Legacy 패키지 검사
- Debug/Release 경고 0 빌드
- 자동 테스트와 필요한 스모크 테스트
- Windows·Ubuntu GitHub Actions 통과
- `CURRENT_STATE.md`와 관련 문서 갱신
- 별도 브랜치와 PR, 검증 후 자동 squash 병합·동기화
