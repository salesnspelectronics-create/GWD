# V67 — Prepared Publish State Sync & Environment Gatekeeper

- Fixed Prepared IIS Publish validation/preflight state ordering: selected path is normalized and committed before validation/preflight.
- Added generation guard so stale prepared-publish UI events cannot overwrite a newer selection.
- Fixed Next validation for Prepared IIS Publish so it validates the prepared publish folder instead of incorrectly requiring a source-build project.
- Environment Scan now records acceptance for IIS and Installers/Runtimes and marks them `Auto verified` in the sidebar.
- Normal Next flow skips IIS and Installers/Runtimes when their actual acceptance criteria are already satisfied. Pages remain directly accessible for inspection/configuration.
- Prepared IIS Publish continues to treat SDK/npm as Not Required while requiring the ASP.NET Core Hosting Bundle.
- SQL is deliberately not auto-passed merely because an engine is detected; administrative connection verification remains required.
- Database and Deployment Location remain gated by their own validation.

## Verification
Static source assertions and ZIP integrity: PASS. Windows/.NET compile and runtime acceptance: PENDING (not executed in this environment).
