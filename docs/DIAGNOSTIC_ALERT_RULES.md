# Diagnostic alert rules / 진단 경고 규칙

Use the existing alert-rule API with `metricType: "diagnostic"`, a `diagnosticId`, and a threshold of `1`.

기존 경고 규칙 API에 `metricType: "diagnostic"`, `diagnosticId`, 임계값 `1`을 사용합니다.

```json
{
  "name": "DB backup stale / DB 백업 지연",
  "metricType": "diagnostic",
  "diagnosticId": "backup:/var/backups/postgres",
  "threshold": 1,
  "severity": "critical",
  "enabled": true
}
```

The rule opens an alert when the matching diagnostic state is not healthy, running, open, current, or not-required. It resolves automatically when the diagnostic recovers.

대상 진단 상태가 healthy, running, open, current, not-required가 아니면 경고를 열고, 정상 상태로 돌아오면 자동 해결합니다.
