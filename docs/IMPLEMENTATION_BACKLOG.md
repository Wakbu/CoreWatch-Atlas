# CoreWatch Atlas 구현 인계 문서

마지막 갱신: 2026-08-01

이 문서는 다음 채팅에서 기능 구현을 이어가기 위한 기준 문서다. 기능을 일부만 구현한 뒤 중간 릴리스하지 말고, 아래 묶음의 구현·테스트·운영 배포·GitHub Release까지 완료한다.

## 이미 배포된 기준 버전

- 운영 Server 및 GitHub Release: `v1.0.6`
- 운영 Server: `100.95.44.33:5443`
- Server 상태: `/health/ready` 정상, SQLite schema version `9`
- v1.0.6 포함 기능:
  - Server/Agent GitHub Release 기반 자동 업데이트
  - Windows/Linux Agent 등록 명령, CA 신뢰 처리, 런타임 설치 보완
  - Agent 업데이트, Server 업데이트, 경고 규칙/Webhook
  - 서버별 7일 요약 보고서 API, 서버 그룹, 유지보수 시간(Webhook 억제), 디스크 추세 예측 API
  - SMTP 일일 요약 Worker 기반 (`Atlas__SmtpReport__*`, 기본 비활성)
  - Dashboard/Server 카드 4열 제한, Atlas 아이콘

## 현재 작업 트리의 미배포 변경

다음 변경은 아직 커밋·릴리스·운영 배포하지 않았다. 다음 작업자는 보존하고 계속 확장한다.

1. 한국어 UI
   - `wwwroot/index.html`: Reports/Groups/Maintenance 메뉴를 `서버 보고서`/`서버 그룹`/`유지보수`로 변경
   - `wwwroot/js/operations.js`: 보고서 화면의 주요 영문을 한국어로 변경

2. Agent 진단 확장
   - `CoreWatch.Atlas.Contracts/MetricsContracts.cs`
     - `SystemMetricsSnapshot.Services`
     - `SystemMetricsSnapshot.Diagnostics`
     - `MonitoredServiceMetrics`, `DiagnosticCheckMetrics`
   - `CoreWatch.Atlas.Agent/DiagnosticsOptions.cs`
     - `Services`, `Processes`, `Containers`, `Urls` 설정 목록
   - `ServiceDiagnostics.cs`: Windows `sc.exe`, Linux `systemctl` 지정 서비스 상태 수집
   - `DiagnosticChecks.cs`: 지정 프로세스, Docker 컨테이너, HTTPS URL 상태 수집
   - `MetricsCollectionWorker.cs`: 기존 기본 수집 이후 진단을 합쳐 Snapshot 전송
   - 기본 수집 실패와 진단 실패는 서로 격리한다.

3. 자산 메타데이터 초안
   - `AssetModels.cs`, `AtlasDatabase.Assets.cs`
   - 담당자(owner), 메모(notes) API:
     - `GET /api/v1/agents/{agentId}/asset`
     - `PUT /api/v1/agents/{agentId}/asset`
   - `AtlasDatabase.CurrentSchemaVersion`은 현재 작업 트리에서 `10`으로 변경됨
   - `asset_metadata` 마이그레이션이 추가됨
   - 태그 연결 테이블과 편집 UI는 아직 미구현

4. 현재 미배포 변경은 마지막 Debug 빌드 및 테스트 `79/79` 통과 후 자산 메타데이터 마이그레이션을 추가했다. 따라서 다음 작업 시작 시 반드시 다시 restore, Debug/Release build, 전체 test를 실행한다.

## 남은 전체 구현 목록

아래는 사용자가 요청한 전체 범위다. 번호별로 따로 완료 보고하지 말고 하나의 제품 확장 릴리스로 끝낸다.

1. 서버 그룹별 Dashboard/보고서
2. 서버별 태그, 담당자, 메모
3. 경고 지속 시간 조건, 재알림, 에스컬레이션
4. 경고 조치 메모, 담당자 배정, 해결 이력
5. Agent 자가진단 및 권한 있는 원클릭 서비스 재시작
6. OS 업데이트 및 재부팅 필요 여부 수집
7. 지정 프로세스, 서비스, 포트 모니터링
8. Docker/컨테이너 상태 및 자원 사용량 수집
9. 파티션별 디스크 용량 예측
10. 네트워크 지연, 패킷 손실, 외부 URL 점검
11. Prometheus/Grafana 연동 강화
12. Slack/Teams/Discord/이메일 알림 채널과 템플릿
13. 사용자, 역할, 감사 로그 관리 화면
14. CSV/PDF 보고서 다운로드와 예약 발송
15. 백업 성공 여부와 최근 백업 시점 모니터링
16. 자산 목록: IP, 역할, OS, 버전, Agent 버전
17. 장애 타임라인 및 원인 후보 요약
18. 다중 Atlas Server 및 고가용성 구성
19. 모바일 반응형 경고 확인 화면
20. 외부 시스템 연동용 API 토큰과 공개 API

## 권장 구현 순서

### 1단계: Agent 및 자산 기반

- 현재 진단 Snapshot 계약을 Server 상세 화면에 표시한다.
- `asset_tags`, `agent_asset_tags` 테이블과 태그 CRUD를 추가한다.
- 자산 편집 화면에서 담당자, 메모, 태그, 역할, IP를 관리한다.
- Agent 설정 UI/API에서 서비스, 프로세스, 컨테이너, URL, 포트, 백업 경로를 지정한다.
- Windows/Linux Collector 또는 별도 진단 Worker에서 OS 업데이트, 재부팅 필요, 포트, Docker, 백업 결과를 수집한다.

### 2단계: 경고와 보고서

- `alert_rules`에 지속 시간, 재알림 간격, 담당자, 그룹/서버 대상 범위를 추가한다.
- 경고 조치 테이블을 만들어 코멘트, 담당자, 해결 시각, 타임라인을 기록한다.
- 유지보수 창은 현재 전역 구현이다. 서버/그룹 범위와 종료 후 재알림 정책으로 확장한다.
- 기간 보고서를 CSV와 PDF로 내보내고, SMTP 예약 발송을 실제 보고서 첨부 방식으로 확장한다.
- 용량 예측은 현재 전체 파일 시스템 합산이다. 파티션별 선형 추세와 임계 날짜를 제공한다.

### 3단계: 연동과 운영

- Prometheus에 Agent 진단 결과와 Alert 상태 메트릭을 추가한다.
- Webhook 채널을 Slack/Teams/Discord 템플릿으로 분리하고, 비밀 URL은 로그에 남기지 않는다.
- API 토큰은 해시로만 저장하고 만료, 범위(scope), 폐기, 감사 로그를 제공한다.
- 모바일 화면은 경고 확인, 담당자 배정, 조치 메모에 집중한다.
- 다중 Server/HA는 SQLite 단일 노드 한계를 먼저 문서화하고 PostgreSQL/공유 Data Protection/로드밸런서 전환 설계를 별도 단계로 처리한다.

## 필수 검증 및 배포

1. `dotnet restore`
2. `dotnet build -c Debug --no-restore`
3. `dotnet test -c Debug --no-build`
4. `dotnet build -c Release --no-restore`
5. `dotnet test -c Release --no-build`
6. `dotnet list package --vulnerable --include-transitive`
7. `scripts/Publish-CoreWatchAtlas.ps1`로 Server/Agent ZIP과 SHA-256 생성
8. ZIP 내부 파일, 크기, SHA-256 확인
9. 운영 Server에 데이터 백업 후 안전 교체, `systemctl is-active corewatch-atlas.service`, `/health/ready` 확인
10. GitHub Release에는 반드시 한글/영문 설명을 실제 줄바꿈으로 작성

## 운영 배포 주의

- 운영 Server 데이터는 `/var/lib/corewatch-atlas`이며 `/opt/corewatch-atlas/data`가 아니다.
- 배포 시 기존 `appsettings.json`과 `runtimes/linux-x64/native/libe_sqlite3.so`를 새 배포 폴더로 복사한다.
- SSH는 `test@100.95.44.33`, sudo는 pty가 필요하다. 자격 증명은 이 문서나 코드에 기록하지 않는다.
- Windows Agent 설치는 관리자 PowerShell이 필수다. LocalMachine Root 인증서 저장소가 서비스 TLS 연결에 필요하다.
- Windows 재설치는 기존 `CoreWatchAtlasAgent` 서비스를 중지하고 프로세스 종료를 기다린 뒤 설치 폴더를 지워야 DLL 잠금을 피할 수 있다.
