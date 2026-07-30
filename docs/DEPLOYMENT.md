# 배포·운영 안내

## Docker

Release 빌드 뒤 `ATLAS_TLS_PASSWORD` 환경 변수와 `deploy/tls/atlas.pfx`를 준비하고 `docker compose up -d --build`를 실행한다. SQLite DB와 Data Protection 키는 `atlas-data` 볼륨에 함께 보존한다. 인증서 비밀번호와 Agent 자격 증명은 Git에 저장하지 않는다.

## 서비스

Linux는 `deploy/linux`의 systemd unit을 `/etc/systemd/system`에 복사하고 전용 계정·데이터 디렉터리·환경 파일을 만든 뒤 `systemctl enable --now`로 시작한다. Windows는 관리자 PowerShell에서 `deploy/windows/Install-CoreWatchService.ps1`로 Server 또는 Agent를 자동 시작 서비스로 등록한다.

## 백업·복구

Server를 중지하거나 SQLite 온라인 백업을 사용한 뒤 DB와 Data Protection `keys` 디렉터리를 항상 한 쌍으로 보관한다. Windows 예시는 `scripts/Backup-CoreWatchAtlas.ps1`이다. 복구는 같은 서비스 계정으로 두 항목을 함께 되돌린 뒤 Server를 시작하고 `/health/ready`가 HTTP 200인지 확인한다.

## 업데이트

새 Release 파일을 별도 디렉터리에 배치하고, DB와 키를 백업한 뒤 서비스를 중지·교체·시작한다. schemaVersion과 Agent 전송·운영자 로그인을 확인하고 이전 배포 파일은 롤백이 확인될 때까지 보관한다.