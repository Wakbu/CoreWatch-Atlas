# CoreWatch-Atlas

CoreWatch-Atlas는 Windows와 Linux 장비의 상태를 한곳에서 확인하기 위한 크로스플랫폼 시스템 모니터링 플랫폼입니다.

> 현재는 설계 및 초기 개발 단계이며 아직 배포 가능한 버전이 없습니다.

## 제품 구분

- **CoreWatch**: 단일 Windows PC를 위한 WPF 기반 로컬 진단·벤치마크·최적화 앱
- **CoreWatch-Atlas**: 여러 Windows·Linux 장비를 위한 에이전트·서버·웹 통합 관제 플랫폼

두 제품은 저장소, 버전, 태그, CI/CD 및 GitHub Release를 독립적으로 관리합니다.

## 목표 구조

```text
Windows Host ─ CoreWatch Atlas Agent ─┐
Linux Host  ─ CoreWatch Atlas Agent ──┼─ Atlas Server/API ─ Atlas Web
Prometheus  ─ 선택적 연동 ────────────┘
```

## 초기 목표

- Windows 및 Linux 공통 시스템 지표 수집
- 여러 장비 등록과 온라인 상태 확인
- 웹 기반 실시간 현황 및 기간별 이력 조회
- Prometheus 호환 `/metrics` 제공
- HTTPS와 장비별 인증
- Docker, systemd 및 Windows Service 배포

초기 버전은 읽기 전용 모니터링에 집중하며 원격 명령 실행과 시스템 최적화는 포함하지 않습니다.

## 예정 구성 요소

```text
CoreWatch.Atlas.Contracts
CoreWatch.Atlas.Agent
CoreWatch.Atlas.Collectors.Windows
CoreWatch.Atlas.Collectors.Linux
CoreWatch.Atlas.Server
CoreWatch.Atlas.Web
```

## 개발 단계

1. 저장소와 공통 빌드 구조 준비
2. Windows/Linux Agent MVP
3. 중앙 Server/API
4. 웹 대시보드
5. 서비스 설치, 인증 및 Docker 배포
6. Prometheus 호환
7. `1.0.0` 안정화

자세한 내용은 [크로스플랫폼 전환 설계](docs/COREWATCH_ATLAS_DESIGN.md)를 참고하세요.

## 버전 정책

- Atlas는 `0.1.0`부터 독립적으로 시작합니다.
- 기존 CoreWatch 버전과 Atlas 버전은 서로 연동하지 않습니다.
- 정식 운영 요구사항을 충족한 뒤 `1.0.0`을 배포합니다.

## 라이선스

CoreWatch-Atlas는 [Apache License 2.0](LICENSE)에 따라 배포됩니다.
