# Prometheus / Grafana 연동

- Agent `/metrics`에는 CPU·메모리·파일시스템·디스크·네트워크 외에 `corewatch_atlas_service_healthy`, `corewatch_atlas_diagnostic_healthy`가 노출된다.
- Server `/metrics/server`에는 severity별 `corewatch_atlas_active_alerts`가 노출된다.
- `deploy/prometheus/corewatch-atlas.yml`은 scrape 예제이며 실제 주소와 TLS·인증 구성을 배포 환경에 맞춰 변경한다.
- `deploy/grafana/corewatch-atlas-dashboard.json`을 Grafana Dashboard 가져오기로 등록하면 기본 자원·진단·경고 패널을 사용할 수 있다.
