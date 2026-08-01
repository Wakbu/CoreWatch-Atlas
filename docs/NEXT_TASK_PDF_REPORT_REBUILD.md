# Next Task: PDF Report Rebuild and Release

## Objective

Replace the current minimal text-only PDF report with a polished operations report, then verify, release, and deploy it.

## Required report content

- Executive header: report target, period, generated time, installed CoreWatch version.
- KPI cards: availability, active/resolved alerts, CPU average/peak, memory average/peak, disk usage.
- Trend visuals: CPU, memory, disk time-series with warning/critical thresholds.
- Alert section: severity, time, status, affected server, and action history where available.
- Capacity section: partition usage, daily growth, projected days until full.
- Server details: OS, Agent version, last collection, snapshot count, and collection gaps.
- CSV export must contain equivalent raw data in readable sections.
- Fleet daily PDF and per-server PDF must use the same visual system.

## Implementation constraints

- Replace `src/CoreWatch.Atlas.Server/ReportExports.cs`; do not retain the current eight-line text PDF layout.
- Keep report generation dependency-light and server-side.
- Add focused tests under `tests/CoreWatch.Atlas.Server.Tests/ReportExportTests.cs` for PDF structure, report sections, CSV data, and attachments.
- Render generated PDFs to PNG during QA and inspect for clipping, overlap, unreadable text, or broken page breaks.
- Preserve UTF-8 CSV BOM and safe PDF escaping.

## Required validation

1. `dotnet restore`
2. Debug build and all tests
3. Release build and all tests
4. `dotnet list package --vulnerable --include-transitive`
5. Generate per-server and fleet sample PDFs, render with Poppler, visually inspect.
6. Build release ZIPs, validate SHA-256 and published-server `/health/ready` smoke test.
7. Publish a new GitHub release with Korean and English release notes.
8. Deploy Server to `100.95.44.33`, verify `active`, installed assembly version, and `/health/ready`.

## Current state

- Production Server is v1.1.3 and healthy.
- Automatic update check interval is set to 15 minutes in `/etc/corewatch-atlas/server.env`.
- Do not leave `Atlas__ServerUpdate__Enabled`, `Version`, `PackageUrl`, or `Sha256` pinned after a bootstrap deployment.
- SMTP real delivery remains unconfigured; use a local SMTP receiver with configurable non-SSL test mode for mail verification.

## 2026-08-02 implementation status

- Report implementation, focused tests, Debug/Release validation, dependency audit, Poppler rendering, ZIP/hash inspection, and local published-server readiness smoke test are complete for v1.1.4.
- GitHub Release publication and deployment to `100.95.44.33` remain after the implementation PR is merged.
