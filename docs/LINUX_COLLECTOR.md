# Linux Collector MVP

`CoreWatch.Atlas.Collectors.Linux`는 Linux Agent에서 읽기 전용으로 시스템 Snapshot을 수집한다. 운영체제 종속 코드는 이 프로젝트 내부에만 둔다.

## 수집 원천

- `/proc/stat`: 100ms 간격의 두 누적 샘플 차이로 CPU 사용률과 논리 프로세서 수 계산
- `/proc/meminfo`: `MemTotal`, `MemAvailable`을 바이트로 변환하며 구형 커널에서는 `MemFree + Buffers + Cached` 사용
- `/proc/diskstats`: 커널 ABI의 512바이트 섹터를 누적 읽기·쓰기 바이트로 변환
- `/proc/net/dev`: 인터페이스별 누적 수신·송신 바이트
- `/proc/uptime`: 부팅 후 경과 시간
- `DriveInfo`: 접근 가능한 마운트의 전체·가용 용량
- `/etc/machine-id`: 장비 ID. 읽을 수 없거나 비어 있으면 `linux:{hostname}` 사용

## 실패 정책

CPU, 메모리와 업타임은 Snapshot의 필수 지표다. 파일 누락, 권한 거부 또는 잘못된 형식은 예외로 전달하며 0으로 위장하지 않는다. 디스크 I/O, 네트워크와 파일 시스템은 제한된 컨테이너에서도 Snapshot을 만들 수 있도록 접근할 수 없는 항목만 빈 컬렉션 또는 제외 항목으로 반환한다. 취소 토큰은 파일 읽기와 CPU 샘플 대기에 전달한다.

## Agent 연결과 테스트

Agent는 Linux에서 `LinuxSystemMetricsCollector`를 자동 등록한다. Windows에서는 Windows Collector가 구현될 때까지 기존 미구성 Collector를 유지한다.

fixture 테스트는 Windows와 Linux에서 파서 단위, 단위 변환, 필수 파일 실패, 선택 파일 권한 제한과 취소를 검증한다. Ubuntu GitHub Actions에서는 실제 호스트의 `/proc`를 읽어 Snapshot 불변 조건을 확인한다.
