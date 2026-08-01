# UI Rendering and Release Policy

## Rendering isolation / 렌더링 격리

- Initial navigation may render the whole selected page once.
- The 15-second background refresh updates the active dashboard-style page only.
- On a server detail page, the refresh path fetches only that server's snapshot history and redraws only the four chart canvases. It must not replace `#content`, rerun operation-page handlers, or recreate unrelated controls.
- New periodic UI work must follow the same rule: isolate the smallest DOM region and avoid full-page rendering for data that has not changed.
- Heavy calculation or rendering that can affect interaction responsiveness must be moved off the main interaction path before adding a new polling loop.

## Code comments / 코드 주석

- Every source module has a top-level responsibility comment.
- Add a short comment at non-obvious boundaries: background workers, persistence and security transitions, update/rollback paths, API-side effects, and UI refresh/render ownership.
- Comments explain why a boundary exists or what it protects; do not restate obvious syntax.

## Release notes / 릴리스 노트

- Every GitHub Release title, summary, and user-visible release note must include Korean and English together.
- State the user impact first, then verification or upgrade notes. Keep package names, versions, SHA-256 values, commands, and URLs identical in both languages.
- Do not publish a release until the release package, its SHA-256 asset, and the published-package smoke test have been verified.
