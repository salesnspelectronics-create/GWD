# V14.18 Verification Report

## Source-level checks performed
- Verified project metadata incremented to 14.18.0 / 14.18.0.0.
- Verified Wizard title and Diagnostic Error Report/Dump identify V14.18.
- Verified Smart Deploy engine now checks `BuildMode == "Smart Deploy"` rather than the legacy `Automatic` value.
- Verified Review & Deploy preserves `Visual Studio prepared` and `Ready publish folder` instead of overwriting them with Smart/Full.
- Verified Offline Build is an extension of the existing `DeploymentService.PublishAsync` pipeline; no second deployment engine was introduced.
- Verified Prepared Publish continues to bypass source compilation/package restore.
- Verified Offline Build uses a project-local `.deployment-offline` root.
- Verified generated offline NuGet configuration clears remote sources and points to local-only package storage/feed.
- Verified offline npm repair command uses `npm install --offline` with the project-local cache.
- Verified offline .NET validation/restore command uses `--configfile <local config>`, `--packages <local packages>`, and `--no-http-cache`.
- Verified Smart Deploy can return using the prior successful binaries without requiring an offline cache when no build stage is required.
- Verified no project-specific dependency names (NSP/ClosedXML/ExcelDataReader) were added to Offline Build logic.
- Verified existing staging/activation/IIS/SQL/health/rollback code paths were not replaced.

## Runtime tests NOT performed
- The execution environment used to prepare this source archive does not contain the .NET SDK/compiler, IIS, SQL Server, Node.js/npm, or a Windows runtime.
- Therefore the WinForms application was **not compiled or launched here**.
- A real `dotnet restore` against the generated local-only config was **not executed here**.
- A real `npm install --offline` was **not executed here**.
- IIS activation, SQL preservation, rollback and health checks were **not runtime-tested here**.

## Required acceptance test on a Windows build/test machine
1. Select a representative ASP.NET Core (+ optional React/Vite) source project.
2. Click `Prepare / Validate Offline Package` while Internet is available.
3. Confirm `<Source>/.deployment-offline/` contains the NuGet package cache/config and (when applicable) npm cache.
4. Disconnect Internet / block api.nuget.org and npm registry.
5. Enable `Offline build` and run Full Deploy. Confirm no Internet dependency source is contacted and build completes from local caches.
6. Repeat with Smart Deploy and no changes. Confirm build/restore stages are skipped safely.
7. Change frontend only and confirm Smart Deploy performs frontend-only work.
8. Remove one required local dependency and confirm Offline Package validation fails quickly with a clear error.
9. Verify Online Build still succeeds with Offline Build disabled.
10. Verify both prepared modes retain their selected mode on Review & Deploy.
