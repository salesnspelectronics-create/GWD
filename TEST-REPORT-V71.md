# V71 test report

PASS statements below are supported by included evidence.

| Check | Result | Evidence |
|---|---|---|
| Original V70 source archive extraction | PASS, 179 files verified | evidence/V70-provenance.json |
| Release solution build, SDK 9.0.315, Windows | PASS, 0 errors / 0 warnings | evidence/build-release.log |
| SQL strategy, fallback, fingerprints, cancellation, concurrency, secrets and application configuration | PASS, 36 assertions | evidence/sql-strategy-tests.log |
| Browse/settings/navigation, profiles/lock, Clean Export, grid rebuilding | PASS, 4 groups | evidence/ui-smoke-tests.log |
| SQL construction boundary, routing, versions, V70 method preservation | PASS, 25 assertions | evidence/static-tests.log |

SQL transport is simulated with an injected opener. These tests do not connect to a live SQL Server and do not prove IIS deployment, SQL permission grants, untrusted-domain behavior on the target machine, async live IIS refresh, 500.30 recovery or actual rollback. No live runtime PASS is claimed. UI smoke tests construct and rebuild controls without changing IIS services.

V70 method hashes cover health checks, IIS configuration, cleanup/rollback, success finalization, startup diagnostics, Clean Export and IIS pages. Normalization removes only V71 redaction wrappers and the diagnostic version label. Baseline hashes: tests/v70-preservation.json.

Reproduce on Windows with .NET 9 SDK:

```powershell
dotnet build DeploymentManager.sln -c Release
dotnet run --project src/DeploymentManager.SmokeTests -c Release --no-build -- --sql-strategy
dotnet run --project src/DeploymentManager.SmokeTests -c Release --no-build
node tests/verify-static.cjs
```

The recorded build used the installed local NuGet cache with -p:RestoreSources=C:\Users\Ali\.nuget\packages -m:1 -nr:false. An initial SDK 10 attempt could not restore missing .NET 9 targeting packs offline; selecting installed SDK 9.0.315 resolved that environmental issue. No target framework or package version was changed.

Manager profile/log/diagnostic secret exclusions do not encrypt the deployed application's required SQL connection settings.
