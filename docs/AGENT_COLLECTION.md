# Agent 메트릭 수집 루프

`CoreWatch.Atlas.Agent`는 운영체제별 Collector를 즉시 한 번 호출한 뒤 설정된 간격마다 Snapshot을 다시 수집한다. 기본 간격은 15초이며 `Atlas:MetricsCollection:Interval`로 변경한다.

## 실행 흐름

1. Windows 또는 Linux Collector가 불변 Snapshot을 생성한다.
2. 성공 Snapshot을 thread-safe 최신 상태 저장소에 교체한다.
3. 설정 시 camelCase 한 줄 JSON을 표준 출력에 기록한다.
4. 설정 시 Prometheus `/metrics`가 같은 최신 Snapshot을 exposition 형식으로 반환한다.
5. 설정 시 영구 Agent ID와 Bearer 자격 증명으로 중앙 Server에 Snapshot을 전송한다.
6. 다음 수집 간격을 기다린다.

수집 오류는 다음 주기에 재시도한다. JSON 출력과 Server 전송 오류는 서로 및 수집 성공과 분리해 격리한다. Server 전송 실패는 로컬 최신 Snapshot·JSON·Prometheus를 중단하지 않으며 다음 주기에 재시도한다. 종료 토큰은 Collector, 출력·전송과 간격 대기에 전달된다.

## 구조화 로그 이벤트

| Event ID | 수준 | 의미 |
|---|---|---|
| 1000 | Information | 수집 루프 시작과 플랫폼·간격 |
| 1001 | Information | 수집 성공과 Agent·플랫폼·시각 |
| 1002 | Error | 수집 실패와 다음 주기 재시도 |
| 1003 | Information | 수집 루프 종료 |
| 1004 | Error | JSON 로컬 출력 실패 격리 |
| 1005 | Error | 중앙 Server 전송 실패 격리 |
| 1010 | Information | Prometheus endpoint 시작 주소 |

설정과 전체 출력 정책은 `docs/LOCAL_OUTPUT.md`를 참고한다.
