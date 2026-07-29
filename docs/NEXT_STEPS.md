# CoreWatch-Atlas 다음 작업

이 문서는 실행 순서와 각 단계의 완료 조건을 관리한다. 한 번에 한 단계만 승인받아 진행하며, 완료 후 `CURRENT_STATE.md`를 갱신한다.

## 1. 공통 메트릭 계약 (완료)

완료일: 2026-07-27

플랫폼 독립 불변 Snapshot 계약과 자동 테스트를 구현했다.

## 2. Collector 추상화와 Agent 연결 (완료)

완료일: 2026-07-27

Collector DI, 수집 주기, 취소 전달, 오류 격리·재시도와 구조화 로그를 구현했다.

## 3. Linux Collector MVP (완료)

완료일: 2026-07-27

`/proc` 기반 기본 지표와 Ubuntu 실환경 검증을 구현했다.

## 4. Windows Collector MVP (완료)

완료일: 2026-07-27

Windows API 기반 기본 지표와 Windows 실환경 검증을 구현했다.

## 5. Agent 로컬 출력과 Prometheus 형식 (완료)

완료일: 2026-07-27

- camelCase 한 줄 JSON Snapshot 진단 출력
- thread-safe 최신 Snapshot 저장
- 선택적 Kestrel `/metrics`, 기본 loopback·비활성화
- 누적 Counter와 제한된 label 정책
- label escape와 64비트 정수 정밀도
- 수집과 출력 오류 격리

## 6A. Server 기반과 SQLite (완료)

완료일: 2026-07-28

- ASP.NET Core 서버 실행 기반
- SQLite 스키마 v1과 멱등 초기화
- 생존·준비 상태와 서비스 상태 API
- 상태 API·스키마 통합 테스트

## 6B-1. 영구 Agent ID와 등록 토큰 (완료)

완료일: 2026-07-28

- 로컬 CLI 기반 일회성 등록 토큰 발급
- 토큰 원문 비저장과 SHA-256 해시 저장
- 만료·1회 소비 처리와 UUIDv7 영구 Agent ID
- 등록 입력 검증과 동시 재사용 방지

## 6B-2. Agent 자격 증명 (완료)

완료일: 2026-07-28

- Agent 자격 증명 발급과 해시 저장
- 요청 인증과 실패 감사 로그
- 자격 증명 교체·폐기

## 6C. Snapshot 수신과 조회 (완료)

완료일: 2026-07-28

- 인증된 Snapshot 수신
- 최신 상태와 SQLite 이력
- 온라인·오프라인 판정
- 보존 기간과 정리 작업

## 7. Web MVP (완료)

완료일: 2026-07-28

- 반응형 통합 대시보드와 접이식 내비게이션
- 전체 장비 카드, 상태 요약, 파생 경고·이벤트·리소스 순위
- 장비별 CPU·메모리·디스크·네트워크 이력 차트
- 검색과 로딩·오류·빈 결과 상태
- 기존 Server API와 동일 프로세스에서 정적 Web 앱 제공

## 8. 운영 보안과 배포

### 8A. 운영자 로그인과 조회 권한 (완료)

완료일: 2026-07-28

- 로컬 CLI 기반 Viewer·Administrator 계정 생성
- PBKDF2 비밀번호 해시, 실패 감사와 5회 실패 시 15분 잠금
- HttpOnly·SameSite 쿠키 로그인과 로그아웃
- 대시보드·조회 API 역할 기반 권한
- 관리자 전용 운영자 목록

### 8B. HTTPS와 비밀정보 관리 (완료)

완료일: 2026-07-29

- 외부 HTTP 거부와 loopback 개발 예외, Kestrel HTTPS 인증서 구성
- 운영자 쿠키 Data Protection 키의 영구 저장과 Windows DPAPI 보호
- Agent 등록·교체 CLI와 Windows DPAPI·Linux 권한 기반 안전 저장
- 로그인·로그아웃 CSRF 방어와 HSTS·CSP 등 운영 보안 헤더

### 8C. 경고·배포·운영

- 경고 규칙 영구 저장, 확인 처리와 알림 채널
- Server·Agent 서비스 패키지와 컨테이너 이미지
- 백업·복구, 업데이트와 부하·보안 검증

## 공통 완료 규칙

- 의도하지 않은 파일 변경 확인
- 의존성 복원과 취약·Legacy 패키지 검사
- Debug/Release 경고 0 빌드
- 자동 테스트와 필요한 스모크 테스트
- Windows·Ubuntu GitHub Actions 통과
- 상태·관련 문서 갱신
- 별도 브랜치와 PR, 검증 후 자동 squash 병합·동기화
