# Windows Collector MVP

`CoreWatch.Atlas.Collectors.Windows`는 Windows Agent에서 읽기 전용으로 시스템 Snapshot을 수집한다. 기존 CoreWatch 코드나 WPF 프로젝트를 참조하지 않으며 Windows 종속 호출은 이 프로젝트 안에만 둔다.

## 수집 원천

- `GetSystemTimes`: 100ms 간격의 두 누적 샘플 차이로 전체 CPU 사용률 계산
- `GlobalMemoryStatusEx`: 전체·가용 물리 메모리
- `GetTickCount64`: 운영체제 업타임
- `DriveInfo`: 준비된 고정 볼륨의 전체·가용 용량
- `IOCTL_DISK_PERFORMANCE`: 최대 32개 물리 디스크의 누적 읽기·쓰기 바이트
- `NetworkInterface.GetIPStatistics`: 인터페이스별 누적 수신·송신 바이트
- Runtime 정보: 호스트명, Windows 설명, 아키텍처와 Agent 버전

장비 ID는 현재 `windows:{hostname}` 형식이다. 서버 등록 단계에서 발급할 영구 장비 ID로 교체할 수 있도록 공통 계약과 분리돼 있다.

## 실패 정책

CPU, 메모리와 업타임은 필수 지표이므로 Windows API 실패나 잘못된 카운터를 예외로 전달하고 0으로 위장하지 않는다. 분리되거나 권한이 제한된 볼륨·물리 디스크·네트워크 인터페이스는 선택 컬렉션에서 제외한다. CPU 샘플 대기와 수집 단계 사이에서 취소 토큰을 확인한다.

## Agent 연결과 테스트

Agent는 Windows에서 `WindowsSystemMetricsCollector`, Linux에서 `LinuxSystemMetricsCollector`를 자동 선택한다. fixture 테스트는 CPU 차이 계산, 카운터 역행, Snapshot 매핑과 취소를 모든 운영체제에서 검증한다. Windows GitHub Actions와 로컬 Windows 테스트는 실제 API로 메모리·CPU·업타임·고정 볼륨·네트워크를 수집해 Snapshot 불변 조건을 확인한다.
