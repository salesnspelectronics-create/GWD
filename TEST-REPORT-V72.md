# V72 test report

Baseline: uploaded V71 Unified SQL Full Source ZIP. Version: 72.0.0. Target: net9.0-windows. Environment: Windows 10, .NET SDK 9.0.318. Tests executed 2026-10-07.

| Check | Result | Evidence |
|---|---|---|
| Release rebuild, all 3 projects | PASS; 0 warnings, 0 errors | evidence/V72-build-release.log |
| Offline dependency restore | PASS; local NuGet cache | evidence/V72-offline-restore.log |
| Workspace regression | PASS; 15 assertions | evidence/V72-workspace-regression.log |
| Static/preservation checks | PASS; 63 assertions | evidence/V72-static-tests.log |
| Unified SQL strategy | PASS; 36 assertions, simulated transport | evidence/V72-sql-strategy-tests.log |
| UI/profile/lock/export/IIS-grid smoke | PASS; 4 groups | evidence/V72-ui-smoke-tests.log |

Regression uses actual production PrepareStableWorkspace and PublishAsync. Synthetic Program.cs contains duplicated /api/v1/health and /api/v1/health/live mappings; incoming removes duplicates but is padded to the same size, with identical timestamp and a manifest already trusting Incoming. Production sync replaces the stale workspace file and verifies equal SHA256. Other checks cover fresh write time, stale untracked .cs removal, explicit cache preservation, full verification of unchanged files, post-sync corruption, mismatch logs with both hashes, failed-manifest promotion, source mutation, corrupt legacy manifest, pipeline block before any external command/staging, concurrent workspace exclusion and cancellation.

The initial online restore attempt failed because network access was restricted. Final build used the existing local cache; package versions and net9 target were unchanged.

Not claimed as PASS: live target SQL Server/IIS deployment, rollback runtime acceptance, or reproduction against the actual ce7c039 application checkout (not present in this manager baseline). Historical evidence logs are retained; current release evidence is prefixed V72-.

Reproduce on Windows: dotnet build DeploymentManager.sln -c Release -m:1 -t:Rebuild; node tests/verify-static.cjs; run DeploymentManager.SmokeTests with --workspace-sync, --sql-strategy, and no arguments for UI smoke.
