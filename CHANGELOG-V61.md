# V61 — Prepared IIS Publish / Direct Deploy

- Added explicit `Prepared IIS Publish` mode on the Project Source page.
- Prepared publish folder is validated as a final ASP.NET Core IIS artifact.
- Required validation: `web.config`, top-level application DLL/EXE, and `wwwroot/`.
- If `BUILD-METADATA.json` exists, `build`, `publish`, and `iis_validation` must all be `PASS`; `runtime_acceptance=PENDING` does not block deployment.
- Prepared mode disables source dependency/workspace actions and never runs dependency resolution, npm/Vite, dotnet restore/build/publish.
- Prepared staging no longer creates source fingerprint/smart-build state.
- Activated prepared deployments are verified as prepared artifacts, not as source builds.
- Deterministic single-folder activation preserves configured production files (default: `appsettings.json`, `appsettings.Production.json`) from the rollback copy.
- Added explicit prepared-mode log markers and separate `PREPARED_PUBLISH_INVALID` validation failure prefix.
- Project-source build modes remain separate and unchanged by the prepared execution branch.
