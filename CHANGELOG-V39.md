# V39 — Fast Local Test Default

## Build modes

- The startup and reset default is now `Fast Local Test`.
- The primary modes are `Fast Local Test`, `Fast IIS Update`, and `Full IIS Deploy`.
- Existing `Visual Studio prepared` and `Ready publish folder` compatibility workflows remain available.

## Fast Local Test

- Uses source/dependency fingerprints from the last successful local build.
- Runs the frontend build only when frontend or frontend dependency input changed.
- Runs `dotnet build -c Debug` incrementally only when backend/migration input changed.
- Never invokes `dotnet publish`.
- Does not stage or replace hosted files, configure/restart IIS, transfer remote files, prepare the database, or prepare rollback.
- Offline dependency validation runs only when dependency fingerprints changed or no trusted successful state exists.
- Fingerprint state is committed only after all required builds succeed.

## Fast IIS Update and Full IIS Deploy

- V38 change-aware hosted deployment is exposed as `Fast IIS Update`.
- The complete publish/IIS path is exposed as `Full IIS Deploy`.
- Offline Only, persistent cache, remote deployment validation, Auto Deploy, cancellation/cleanup, health check, rollback, stale-UI verification, and prepared-source modes remain intact.

## Diagnostics and history

- Deployment History keeps expandable per-stage timing with DONE/SKIPPED/FAILED status and detail.
- Added `Export Diagnostic Bundle` to Deployment History.
- The ZIP includes sanitized history, recorded stage timings, current session log, fingerprint manifests, dependency-cache presence, and runtime information.
- Password/token/secret/API-key/connection-string patterns are redacted during export.

## Log viewer

- View Log opens on the latest compact tail rather than replaying a large file from the beginning.
- Added `Jump to Latest`.
- Following stops when the user scrolls away from the end.

## Version

- Product, assembly, and file version: `39.0.0`.
