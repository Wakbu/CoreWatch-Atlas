# CoreWatch-Atlas 다음 작업

이 문서는 실행 순서와 각 단계의 완료 조건을 관리한다. 한 번에 한 단계만 승인받아 진행하며, 완료 후 `CURRENT_STATE.md`를 갱신한다.

## 1. 공통 메트릭 계약

목표: Windows와 Linux Collector가 동일하게 반환할 플랫폼 독립 데이터 계약을 정의한다.

예정 범위:

- `ISystemMetricsCollector` 비동기 인터페이스
- `SystemMetricsSnapshot`과 장비 식별 정보
- CPU 사용률과 논리 프로세서 수
- 전체·사용·가용 메모리 바이트
- 파일 시스템별 전체·가용 바이트
- 디스크별 읽기·쓰기 누적 바이트
- 네트워크 인터페이스별 송수신 누적 바이트
- 업타임과 UTC 수집 시각
- 단위, null 허용 범위, 값의 불변 조건 문서화
- 계약 생성·검증 자동 테스트

제외 범위:

- Windows/Linux 실제 수집 구현
- 온도·GPU·SMART
- 서버 전송과 `/metrics`
- 원격 제어

완료 조건:

- Contracts와 Tests만 변경
- 경고 0 Debug/Release 빌드
- 정상값·경계값·잘못된 값 테스트
- Windows·Ubuntu CI 통과

## 2. Collector 추상화와 Agent 연결

- Collector DI 등록
- 주기적 Snapshot 수집
- 취소와 예외 격리
- 구조화 로그
- 테스트용 fake collector
- 아직 서버 전송은 하지 않음

## 3. Linux Collector MVP

- `/proc/stat`, `/proc/meminfo`, `/proc/diskstats`, `/proc/net/dev`, `/proc/uptime`
- Ubuntu 통합 테스트 fixture
- 권한 부족과 파일 누락 처리

## 4. Windows Collector MVP

- CPU, 메모리, 고정 디스크, 네트워크, 업타임
- Windows 종속 코드는 Collector 프로젝트로 격리
- 기존 CoreWatch 코드 직접 참조 금지

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
- 기간별 이력
- 반응형 UI

## 공통 완료 규칙

각 단계에서 다음을 모두 수행한다.

- 의도하지 않은 파일 변경 확인
- 의존성 복원
- 취약·Legacy 패키지 검사
- Debug/Release 경고 0 빌드
- 자동 테스트와 필요한 스모크 테스트
- Windows·Ubuntu GitHub Actions 통과
- `CURRENT_STATE.md`와 이 문서 갱신
- 별도 브랜치, Draft PR, 승인 후 squash 병합
