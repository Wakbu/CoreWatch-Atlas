# Agent 로컬 출력과 Prometheus

Agent는 수집한 최신 Snapshot을 로컬 진단용 JSON으로 출력하고, 선택적으로 Prometheus exposition endpoint를 제공한다. 서버 전송 기능과 독립적으로 동작한다.

## 설정

```json
{
  "Atlas": {
    "MetricsCollection": {
      "Interval": "00:00:15"
    },
    "LocalOutput": {
      "JsonEnabled": true,
      "Prometheus": {
        "Enabled": false,
        "Url": "http://127.0.0.1:9464"
      }
    }
  }
}
```

JSON은 기본 활성화되며 Snapshot 하나를 camelCase 한 줄 JSON으로 표준 출력에 기록한다. `JsonEnabled`를 `false`로 바꿔도 최신 Snapshot 저장과 Prometheus 출력은 계속 동작한다. 출력 실패는 Event ID 1004로 격리하며 수집 루프를 중단하지 않는다.

Prometheus endpoint는 기본 비활성화다. 활성화하면 설정한 HTTP origin의 `/metrics`에서 최신 Snapshot을 반환한다. 첫 수집 전에는 HTTP 503을 반환한다. 기본 주소는 loopback이므로 외부에서 접근할 수 없다. 외부 주소로 바인딩할 경우 현재 인증·TLS가 없으므로 방화벽과 사설망으로 접근을 제한해야 한다.

환경 변수 예시:

```shell
Atlas__LocalOutput__Prometheus__Enabled=true
Atlas__LocalOutput__Prometheus__Url=http://127.0.0.1:9464
```

## Prometheus 지표

- CPU 사용률·논리 프로세서 수
- 전체·가용 메모리와 업타임
- 최신 Snapshot UTC timestamp
- Agent 정보
- 파일 시스템별 전체·가용 바이트
- 디스크별 누적 읽기·쓰기 바이트 Counter
- 네트워크 인터페이스별 누적 송수신 바이트 Counter

label은 Agent 정보, 파일 시스템 ID·mount, 디스크와 네트워크 장치로 제한한다. 프로세스·PID처럼 빠르게 변하는 고카디널리티 label은 제공하지 않는다. label의 backslash, quote와 newline은 Prometheus 규칙에 맞게 escape하며 64비트 누적 Counter는 정수 정밀도를 유지한다.
