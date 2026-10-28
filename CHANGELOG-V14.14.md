# V14.15 — Smart Safe Deploy

- Automatic mode now records SHA-256 source-state in the last successful hosted deployment.
- Frontend-only changes reuse the last successful backend binaries and rebuild/copy only frontend wwwroot.
- No-change redeploy can reuse the last successful binaries.
- Any backend, DbInitializer/migration, missing state, or uncertain state automatically falls back to full dotnet publish.
- Dependency changes trigger npm repair/install before frontend build.
- Added Full rebuild mode to force the traditional complete compilation path.
- Existing staging, rollback, cancellation, IIS setup, DB setup, and health check remain in place.
