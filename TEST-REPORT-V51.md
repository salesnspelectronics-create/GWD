# V51 Test Report

Executed on 2026-09-09 against the Release build. No real IIS deployment, publish, backup, or rollback was performed.

| Test | Result |
|---|---|
| `dotnet restore DeploymentManager.sln` | PASS — up to date |
| `dotnet build DeploymentManager.sln -c Release --no-restore` | PASS — 0 warnings, 0 errors |
| `projects list --output json` | PASS — valid JSON, 2 profiles returned |
| `project show --project "NSP Office"` | PASS |
| Missing project lookup | PASS — exit code 9 |
| `validate --project "NSP Office"` (non-admin) | PASS — safely refused with exit code 8 |
| `deploy --dry-run --non-interactive --output json` | PASS — no changes; result file with spaces created |
| `status --project "NSP Office"` (non-admin) | PASS — safely refused with exit code 8 |
| `health-check --project "NSP Office" --output json` | PASS — HTTP 200, one attempt |
| `backups list --project "NSP Office"` | PASS — empty list returned |

Dry-run output included the planned validate → build/publish → artifact validation → backup → activation → health-check → automatic rollback sequence and reported `readyForActualDeploy: false` because IIS administrator access was unavailable.
