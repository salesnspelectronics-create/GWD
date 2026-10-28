# V61 Test Report

## Environment limitation
The current build environment does not contain the .NET SDK (`dotnet` command unavailable). Therefore no compile, Windows IIS runtime test, or owner acceptance is claimed here.

## Static checks performed
- Version centralized at 61.0.0.
- Prepared IIS Publish is an explicit source-page mode.
- Prepared validation requires web.config + wwwroot + a top-level DLL/EXE.
- BUILD-METADATA.json, when present, requires build=PASS, publish=PASS, iis_validation=PASS.
- Prepared branch is reached before dependency/frontend/backend build stages.
- Prepared branch records dependency/frontend/backend/publish stages as skipped.
- Prepared branch copies validated output to deployment staging and returns immediately.
- Prepared post-deploy verification does not require `.smart-deploy-state.json` and does not compute source fingerprints.
- Production configuration preservation is explicit during single-folder activation.
- Brace-balance/static text checks passed for modified C# files.

## Required Windows acceptance
1. Compile V61 and fix any compile errors.
2. Select NSP Office `IIS-Publish` using Prepared IIS Publish.
3. Confirm validation PASS.
4. Confirm no npm, Vite, dotnet restore/build/publish runs.
5. Confirm backup, IIS stop, deterministic file replacement, IIS start, and health check.
6. Force a deployment/health failure and verify rollback behavior.
7. Regression-test Project Source mode.
