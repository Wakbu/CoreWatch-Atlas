# CoreWatch Atlas 다중 Server 및 고가용성 설계

## 현재 지원 범위

Atlas Server는 SQLite 단일 writer 구조다. SQLite 파일을 NFS/SMB로 여러 Server가 동시에 열거나 여러 인스턴스를 같은 데이터 디렉터리에 연결하는 구성은 지원하지 않는다. 현재 배포에서는 Server 한 대와 정기 DB·Data Protection 키 백업을 사용한다.

## 권장 운영 구성

1. L4/L7 로드밸런서는 단일 활성 Server로만 전달하고 예비 Server는 대기시킨다.
2. `/var/lib/corewatch-atlas`의 SQLite DB, Data Protection 키, 업데이트 상태를 같은 시점에 백업한다.
3. 장애 시 활성 Server를 격리한 뒤 백업을 예비 Server에 복원하고 `/health/ready`가 schema version 11을 반환한 후 트래픽을 전환한다.
4. Agent는 로드밸런서의 고정 HTTPS 주소만 사용한다. 인증서 SAN과 CA는 두 Server에서 동일하게 검증 가능해야 한다.

## Active-Active 전환 조건

Active-Active는 PostgreSQL 저장소 구현, 공유 Data Protection 키 저장소, 분산 작업 잠금, 알림·SMTP·정리 Worker leader election, Agent 명령의 원자적 claim, 세션 또는 중앙 인증 저장소가 모두 준비된 뒤 활성화한다. SQLite 상태에서 `replica > 1`만 설정하는 방식은 데이터 손상과 중복 알림 위험 때문에 금지한다.

## 복구 검증

- 백업 DB에 `PRAGMA integrity_check`를 실행한다.
- `/health/live`, `/health/ready`, 운영자 로그인, Agent Snapshot 업로드를 확인한다.
- 대기 중이던 Agent 명령과 알림 발송이 중복 처리되지 않는지 확인한다.
