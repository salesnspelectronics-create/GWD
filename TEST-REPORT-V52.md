# V52 Test Report

Executed on 2026-09-09. Only tests actually executed for V52 are listed here. No real IIS deployment, publish of NSP Office, backup, target-file replacement, or rollback was performed.

| Test | Result |
|---|---|
| Solution restore | PASS |
| Solution Release build | PASS — 0 errors |
| V52 CLI and GUI file versions | PASS — `52.0.0.0` |
| `projects list --output json` | PASS — exit 0 |
| `project show --project "NSP Office"` | PASS — exit 0 |
| Missing project | PASS — exit 9 |
| `validate` without Administrator | PASS — safely refused, exit 8 |
| Full-mode `deploy --dry-run --non-interactive --output json` | PASS — exit 0; no changes |
| JSON result file with a path containing spaces | PASS |
| `status` without Administrator | PASS — safely refused, exit 8 |
| `health-check` | PASS — HTTP 200, exit 0 |
| `backups list` | PASS — exit 0 |
| Per-project concurrent-operation lock | PASS — second operation returned exit 10 |
| Wrapper success exit-code passthrough | PASS — exit 0 |
| Wrapper failure exit-code passthrough | PASS — exit 9 |
| WinForms Review → cleanup → Project Source smoke test | PASS — 3 cycles; reusable Log TextBox remained alive |
| `publish-latest.ps1` | PASS — restore/publish completed before refreshing `latest` |

The WinForms test executes the exact control-lifetime sequence that caused the V50 crash. Direct visual mouse automation was unavailable in this environment, so no claim is made that a human-visible click test was executed.
