# Agent 메트릭 수집 루프

`CoreWatch.Atlas.Agent`는 DI로 등록된 `ISystemMetricsCollector`를 즉시 한 번 호출한 뒤 설정된 간격마다 다시 호출한다. Snapshot 전송과 로컬 출력은 아직 포함하지 않는다.

## 등록과 설정

운영체제 Collector는 다음 등록 확장을 사용한다.

```csharp
services.AddAtlasMetricsCollection<LinuxSystemMetricsCollector>(configuration);
```

수집 간격의 기본값은 15초이며 `appsettings.json`에서 변경한다.

```json
{
  "Atlas": {
    "MetricsCollection": {
      "Interval": "00:00:15"
    }
  }
}
```

간격은 0보다 커야 한다. 현재 실행 프로젝트는 실제 Collector가 추가될 때까지 `UnconfiguredSystemMetricsCollector`를 등록한다. 이 구현은 임의의 0 값 Snapshot을 만들지 않고 명확한 `NotSupportedException`을 발생시키며, Worker가 이를 격리하고 다음 주기에 재시도한다.

## 실행 정책

- 호스트 종료 토큰을 Collector에 그대로 전달한다.
- 종료 요청으로 발생한 취소는 오류로 기록하지 않는다.
- 한 번의 수집 실패는 Worker를 종료시키지 않고 다음 간격에 재시도한다.
- 간격 대기는 `TimeProvider`를 사용하여 테스트와 향후 시간 제어를 지원한다.
- 성공한 Snapshot은 현재 로그만 남기며 서버로 전송하지 않는다.

## 구조화 로그 이벤트

| Event ID | 수준 | 의미 |
|---|---|---|
| 1000 | Information | 수집 루프 시작과 플랫폼·간격 |
| 1001 | Information | 수집 성공과 Agent ID·플랫폼·UTC 시각 |
| 1002 | Error | 수집 실패와 다음 주기 재시도 |
| 1003 | Information | 수집 루프 종료 |

## 자동 테스트 범위

- 설정값과 Collector·Hosted Service DI 등록
- 첫 수집 실패 후 다음 주기 정상 재시도
- 구조화 오류 로그의 Event ID와 `Platform` 속성
- 활성 수집 중 호스트 종료 시 취소 전달
- 0 이하 수집 간격 거부
