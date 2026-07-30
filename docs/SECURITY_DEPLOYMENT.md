# CoreWatch-Atlas HTTPS·비밀정보 운영

## 보안 기본값

Server는 `Atlas:Security:RequireHttps=true`가 기본이며 HTTPS가 아닌 외부 요청을 `426 Upgrade Required`로 거부한다. 개발 편의를 위해 기본값에서는 loopback HTTP만 허용한다. 운영에서는 `Atlas__Security__AllowLoopbackHttp=false`로 설정한다.

HTTPS 응답에는 HSTS, CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`를 적용한다. 로그인·로그아웃은 `GET /api/v1/auth/csrf`에서 받은 토큰을 `X-CoreWatch-CSRF` 헤더로 보내야 한다. Agent Bearer API에는 쿠키를 사용하지 않으므로 이 CSRF 절차를 적용하지 않는다.

## HTTPS 인증서

Atlas는 Kestrel의 표준 HTTPS 구성을 사용하며 인증서 검증을 우회하지 않는다. 운영 인증서는 신뢰할 수 있는 내부 CA 또는 공인 CA에서 발급하고 PFX 비밀번호는 설정 파일이나 Git에 저장하지 않는다.

환경 변수 예시:

```shell
ASPNETCORE_URLS=https://0.0.0.0:5443
ASPNETCORE_Kestrel__Certificates__Default__Path=/etc/corewatch-atlas/tls/atlas.pfx
ASPNETCORE_Kestrel__Certificates__Default__Password=<secret-manager에서 주입>
Atlas__Security__AllowLoopbackHttp=false
```

로컬 개발 인증서는 `dotnet dev-certs https --trust`로 신뢰시킬 수 있다. 이 인증서는 운영 서버에 사용하지 않는다.

## Server Data Protection 키

운영자 세션 쿠키와 CSRF 토큰은 `Atlas:Security:DataProtectionKeyPath`에 영구 저장한 Data Protection 키로 보호한다. 기본 경로는 Server 콘텐츠 루트 아래 `data/data-protection-keys`다.

- Windows: 키 파일을 현재 Windows 계정의 DPAPI로 암호화한다. 서비스를 실행할 전용 계정을 고정하고 이 계정으로 키 디렉터리를 백업·복원한다.
- Linux: 키 디렉터리를 소유자 전용 `0700`으로 만든다. 전용 서비스 계정만 접근하게 하고 암호화된 볼륨 또는 OS 비밀 저장소와 함께 사용한다.
- 여러 Server 인스턴스: 모든 인스턴스가 같은 키 저장소와 application name을 공유해야 한다. 외부 키 저장소 연동은 8C 배포 단계의 남은 작업이다.

키 디렉터리를 잃으면 기존 운영자 쿠키는 무효화된다. 다른 Windows 계정이 만든 DPAPI 키는 복호화할 수 없으므로 서비스 계정을 임의로 바꾸지 않는다.

## Agent 등록과 자격 증명

Server를 정지한 상태에서 일회성 토큰을 만든 뒤 대상 Agent에서 등록한다.

```shell
dotnet run --project src/CoreWatch.Atlas.Server/CoreWatch.Atlas.Server.csproj -c Release -- --create-registration-token
dotnet run --project src/CoreWatch.Atlas.Agent/CoreWatch.Atlas.Agent.csproj -c Release -- --register-agent https://atlas.example.internal:5443
```

대화형 입력이 불가능한 자동 배포에서는 토큰을 프로세스 환경 변수 `COREWATCH_ATLAS_REGISTRATION_TOKEN`으로 한 번만 주입한다. 로그·명령행·설정 파일에는 토큰을 남기지 않는다.

등록 결과는 `Atlas:CredentialStore:Path` 아래 `credentials.protected`에 저장되며 이후 Agent는 별도 평문 설정 없이 자동으로 불러온다.

- Windows: Agent 전용 Data Protection 키를 DPAPI로 보호한다.
- Linux: 저장소 디렉터리는 `0700`, 자격 증명 파일은 `0600`으로 제한한다.
- 손상·변조·복호화 실패 시 자격 증명을 사용하지 않는 fail-closed 방식이다.
- HTTPS만 허용하며 개발용 loopback HTTP만 예외다.

자격 증명 교체:

```shell
dotnet run --project src/CoreWatch.Atlas.Agent/CoreWatch.Atlas.Agent.csproj -c Release -- --rotate-agent-credential
```

교체 응답을 안전 저장한 뒤에만 다음 실행에서 새 자격 증명을 사용한다. 저장소 전체를 Agent 서비스 계정 소유로 유지하고 Git, 이미지와 일반 백업에 평문으로 포함하지 않는다.

## 운영 전 확인

- 운영 CA 인증서와 전체 인증서 체인을 Agent에서 검증한다.
- 외부 HTTP 예외를 끄고 방화벽에서 HTTPS 포트만 연다.
- Server와 Agent를 각각 고정된 최소 권한 계정으로 실행한다.
- Data Protection 키와 SQLite DB의 백업·복원 절차를 함께 시험한다.
- Prometheus endpoint를 활성화한다면 loopback 또는 별도 인증·TLS 프록시로 제한한다.

기존 Agent 저장소에 자격 증명이 있으면 `--register-agent`는 유효한 일회성 등록 토큰으로 같은 Agent ID의 자격 증명만 재발급한다. 이 복구 경로는 중복 Agent를 만들지 않는다.