# CHANGELOG V14.28 — Safe Fast Deploy / Stale UI Protection

- Root cause reviewed in V14.27 Fast Build state logic.
- Fast Deploy now uses a persistent per-project build-state under Deployment Repository/BuildState, outside source and Deployment Manager version folders.
- Frontend fingerprint covers all discovered package roots and build-affecting source/config/public/assets files while excluding generated output/cache directories.
- Frontend dependency fingerprint includes npm/yarn/pnpm lock files.
- Frontend build output is detected from common wwwroot/dist/build/out locations and explicitly overlaid into staging after dotnet publish.
- Fast Deploy never treats an existing dist/wwwroot as proof that the frontend is current.
- Added frontend output hash and post-activation verification. A mismatch fails deployment with STALE FRONTEND DETECTED instead of reporting full success.
- Persistent state is committed only after health + verification succeed.
- Fast Build remains the default. Persistent NuGet/npm cache behavior is preserved.
- Legacy hosted .smart-deploy-state.json is imported for compatibility when no repository state exists.
- Remote IIS keeps health verification; file-level output fingerprint verification is currently local-IIS only.
