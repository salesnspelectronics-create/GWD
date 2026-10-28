# V14.18 — Offline Build Cache + Smart Deploy Pipeline Fixes

## Added
- Added **Offline build — use project-local dependency cache** to the existing source-build pipeline.
- Added **Prepare / Validate Offline Package**. Preparation may use Internet once to place .NET and npm dependencies under `<Source>/.deployment-offline/`; subsequent Offline Build uses only that project-local cache.
- Added local-only `NuGet.Offline.config` generation with cleared Internet package sources.
- Added project-local NuGet global package cache, npm cache, and offline package metadata.
- Added pre-deployment Offline Package validation. Offline deployment is blocked when required local dependencies are unavailable.
- Offline logs explicitly report Build Mode, offline state, local NuGet config/packages, frontend strategy, and validation PASSED/FAILED.
- Offline npm repair uses `--offline`; offline .NET restore uses the local-only NuGet config and `--no-http-cache`.

## Fixed
- Fixed Smart Deploy pipeline activation: UI used `Smart Deploy` while the engine incorrectly checked for legacy `Automatic`.
- Fixed Review & Deploy so `Visual Studio prepared` and `Ready publish folder` are preserved instead of being silently rewritten to Smart/Full.

## Preserved
- Existing staging, backup, IIS, SQL/database preservation, rollback/recovery, health check, cancellation, logging, and prepared-publish deployment pipeline remain in place.
- No project-specific package names are hard-coded.

## Notes
- Prepared Publish remains a no-build path and does not require Offline Build caches.
- `Prepare / Validate Offline Package` is the explicit online preparation action. Checking Offline Build itself does not permit Internet fallback during deployment.
