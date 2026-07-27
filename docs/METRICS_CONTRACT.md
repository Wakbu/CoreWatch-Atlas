# 공통 메트릭 계약

Windows와 Linux Collector는 `CoreWatch.Atlas.Contracts`의 동일한 계약을 반환한다. 모든 모델은 생성 후 변경할 수 없으며, 컬렉션 입력은 Snapshot 생성 시 복사된다.

## 단위와 의미

| 값 | 단위·범위 | 의미 |
|---|---|---|
| `CapturedAtUtc` | UTC `DateTimeOffset` | 수집이 완료된 시각 |
| `Uptime` | 0 이상의 `TimeSpan` | 운영체제 부팅 후 경과 시간 |
| `Cpu.UsageRatio` | 유한한 0~1 실수 | 전체 논리 프로세서 평균 사용 비율 |
| `LogicalProcessorCount` | 1 이상의 정수 | 운영체제가 노출한 논리 프로세서 수 |
| 메모리·파일 시스템 용량 | 바이트 | 전체·가용 값이며 사용량은 `전체 - 가용`으로 계산 |
| 디스크 I/O | 누적 바이트 | 장치별 읽기·쓰기 누적값 |
| 네트워크 I/O | 누적 바이트 | 인터페이스별 수신·송신 누적값 |

누적값의 초당 변화량은 Collector가 아니라 이후 처리 계층에서 두 Snapshot의 차이로 계산한다.

## 필수값과 불변 조건

- 장비 ID, 호스트명, 운영체제, 아키텍처, 에이전트 버전과 장치 키는 null·빈 문자열·공백일 수 없다.
- Snapshot의 Agent, CPU, Memory와 세 메트릭 컬렉션은 null일 수 없다.
- 컬렉션 원소는 null일 수 없고, 파일 시스템 ID·디스크 장치명·네트워크 인터페이스명은 각 컬렉션 안에서 대소문자를 구분하여 유일해야 한다.
- 전체 메모리와 파일 시스템 전체 용량은 0보다 커야 하며 가용 용량은 전체 용량보다 클 수 없다.
- 디스크와 네트워크 카운터는 `ulong`이며 음수를 허용하지 않는다. 장치 또는 에이전트 재시작 시 누적값 감소를 정상적인 카운터 리셋으로 처리해야 한다.
- 파일 시스템·디스크·네트워크 컬렉션은 비어 있을 수 있다. 제한된 컨테이너나 권한 부족 환경에서도 Snapshot 자체를 표현하기 위해서다.

## 오류와 취소

잘못된 값은 모델 생성 시 `ArgumentException` 또는 `ArgumentOutOfRangeException`으로 거부한다. `ISystemMetricsCollector.CaptureAsync` 구현은 전달된 `CancellationToken`을 준수하고, 운영체제 접근 실패를 유효한 0 값으로 위장하지 않는다.