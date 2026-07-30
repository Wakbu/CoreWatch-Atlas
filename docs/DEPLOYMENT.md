# 배포·운영 안내

## Docker

Release 빌드 뒤 `ATLAS_TLS_PASSWORD` 환경 변수와 `deploy/tls/atlas.pfx`를 준비하고 `docker compose up -d --build`를 실행한다. SQLite DB와 Data Protection 키는 `atlas-data` 볼륨에 함께 보존한다. 인증서 비밀번호와 Agent 자격 증명은 Git에 저장하지 않는다.

## 서비스

Linux는 `deploy/linux`의 systemd unit을 `/etc/systemd/system`에 복사하고 전용 계정·데이터 디렉터리·환경 파일을 만든 뒤 `systemctl enable --now`로 시작한다. Windows는 관리자 PowerShell에서 `deploy/windows/Install-CoreWatchService.ps1`로 Server 또는 Agent를 자동 시작 서비스로 등록한다.

## 백업·복구

Server를 중지하거나 SQLite 온라인 백업을 사용한 뒤 DB와 Data Protection `keys` 디렉터리를 항상 한 쌍으로 보관한다. Windows 예시는 `scripts/Backup-CoreWatchAtlas.ps1`이다. 이 스크립트는 SQLite의 `-wal`·`-shm` 동반 파일도 함께 보관하고 모든 백업 파일의 SHA-256 목록을 만든다. 복구는 같은 서비스 계정으로 DB 파일들 및 `keys`를 함께 되돌린 뒤 Server를 시작하고 `/health/ready`가 HTTP 200인지 확인한다.

## 업데이트

새 Release 파일을 별도 디렉터리에 배치하고, DB와 키를 백업한 뒤 서비스를 중지·교체·시작한다. schemaVersion과 Agent 전송·운영자 로그인을 확인하고 이전 배포 파일은 롤백이 확인될 때까지 보관한다.

Agent 자동 업데이트는 Server의 `Atlas:AgentUpdate`에 대상 버전, 절대 HTTPS 패키지 URL과 SHA-256을 설정한 뒤 관리자 화면에서 Agent별로 승인한다. Agent는 승인된 배포만 가져오며 다운로드·해시·ZIP 경로·패키지 버전을 검증한 후 백업과 교체를 수행한다. Linux unit은 설치 디렉터리 쓰기 권한과 `Restart=on-failure`가 필요하고, Windows 서비스는 업데이트 helper가 서비스를 다시 시작할 수 있는 계정으로 실행해야 한다.

자체서명 인증서를 사용하는 Server의 Agent 등록 명령은 첫 단계에서만 `curl -k` 또는 PowerShell `-SkipCertificateCheck`으로 공개 인증서를 받아 OS 신뢰 저장소에 등록합니다. 이후 설치 스크립트·패키지·Agent 등록은 등록한 CA로 정상 검증합니다. 생성된 명령을 수정하거나 CA 등록 단계를 건너뛰지 않습니다.

## Release 산출물

Release 빌드와 테스트가 통과한 작업 트리에서 `scripts/Publish-CoreWatchAtlas.ps1 -OutputDirectory <경로>`를 실행한다. Server와 Agent의 framework-dependent ZIP, 각 ZIP의 SHA-256 파일을 만든다. 대상 호스트에는 .NET 10 ASP.NET Core Runtime(Server) 또는 .NET 10 Runtime(Agent)이 필요하다.
