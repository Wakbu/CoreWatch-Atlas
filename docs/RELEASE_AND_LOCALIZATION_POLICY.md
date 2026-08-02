# Release and localization policy / 릴리스 및 현지화 정책

## Required release notes / 필수 릴리스 노트

- Every release title and body must present Korean and English together, with the same meaning and the same technical values.
- 모든 릴리스 제목과 본문은 한국어와 영어를 함께 제공하며, 의미와 버전·SHA-256·명령·URL 값이 서로 일치해야 합니다.
- Use an actual Markdown file with `gh release create --notes-file <file>`. Do not pass escaped `\n` text to `--notes`; GitHub will display it literally instead of creating lines.
- `gh release create --notes`에 이스케이프된 `\n` 문자열을 넘기지 않습니다. 실제 줄바꿈이 포함된 Markdown 파일을 만들고 `--notes-file <file>`로 발행해야 GitHub에서 정상적으로 줄바꿈됩니다.
- Release notes start with user impact, then include verification, upgrade/rollback notes, and package checksums.
- 릴리스 노트는 사용자 영향부터 쓰고, 검증 결과, 업그레이드·롤백 안내, 패키지 SHA-256 순으로 작성합니다.

## User-facing UI / 사용자 화면

- New user-visible workflow labels must be bilingual: `한국어 / English`.
- 새 사용자 화면의 워크플로 라벨은 `한국어 / English` 형식으로 병기합니다.
- Forms must use explicit labels and responsive layout; checkboxes must never inherit the normal text-input width.
- 폼에는 명시적 레이블과 반응형 레이아웃을 사용하며, 체크박스가 일반 텍스트 입력 폭을 상속받지 않도록 합니다.

## Background refresh isolation / 백그라운드 갱신 격리

- Graph refreshes must update only the active dashboard/chart DOM region. They must not rerender another page, recreate forms, or reset the user's current selection, input, scroll position, or expanded action panel.
- 그래프 갱신은 현재 Dashboard·차트 영역만 갱신해야 합니다. 다른 페이지를 다시 렌더링하거나 폼을 재생성하여 사용자가 선택한 항목, 입력값, 스크롤 위치, 펼쳐 둔 조치 패널을 초기화해서는 안 됩니다.
- Before adding polling or a timer, identify the smallest DOM region that can change. Preserve interaction state unless the user explicitly navigates away or submits a successful action.
- 폴링이나 타이머를 추가하기 전에 변경 가능한 최소 DOM 영역을 식별합니다. 사용자가 직접 페이지를 이동하거나 작업을 성공적으로 제출한 경우 외에는 상호작용 상태를 유지합니다.
