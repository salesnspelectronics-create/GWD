# Generic Web Deployment Manager V43

## IPv4 control

- Replaced the unrestricted remote host TextBox with a four-segment IPv4 control.
- The control visibly separates octets with three dots.
- Each octet accepts digits only and is clamped to the valid IPv4 range `0..255`.
- Typing three digits or a dot advances to the next octet; arrow/backspace navigation is supported.
- Remote deployment still requires all four valid octets before Next is enabled.

## Source-first wizard

- The first wizard page is now **Project Source** rather than general deployment settings.
- Selecting a release/package locates its ASP.NET Core Web project and canonical source root.
- Application, IIS site and database identities are derived and displayed before proceeding.
- Deployment target, repository, workspace and build settings are handled on the next page.

## Unified diagnostics

- Added one **Diagnostics Center** entry point for:
  - current log tail;
  - deployment history and stage timings;
  - latest build performance;
  - application error reports;
  - crash reports;
  - complete diagnostic ZIP export to Desktop.
- The current log opens directly at its latest portion without replaying/scanning visibly from the first line.
- Added process-level UI-thread and AppDomain crash capture under ProgramData.
- Diagnostic ZIP now includes sanitized crash and error reports.

## Preserved

- V42 stable source workspace, source synchronization and cache preservation.
- Offline Only default with optional readiness test.
- Online/Hybrid behavior, Fast Local Test, IIS deployment modes, history, cancellation, rollback and health checks.
