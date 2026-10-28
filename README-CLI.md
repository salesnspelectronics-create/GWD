# Deployment Manager CLI V54

`DeploymentManager.exe` is the official headless PowerShell entry point. It shares the deployment services used by the WinForms application and never opens a window, MessageBox, prompt, or file picker.

## First-time project registration

Open `GenericWeb.DeploymentManager.exe` once as Administrator, select the source and deployment settings, and start a deployment or Fast Local Test. V54 saves a secret-free project profile under `%ProgramData%\NSP\DeploymentManager\Projects`. Passwords and tokens are never stored in this project profile.

The stable release location is configured in the GUI under **Settings → Manager release/output folder** (or with `GWDM_MANAGER_ROOT`). Run `publish-latest.ps1` after a successful source update; it refreshes `latest` only after restore and publish succeed.

For isolated automation/tests, `GWDM_APPDATA_ROOT` can select a different settings root.

## Commands

```powershell
& ".\DeploymentManager.exe" projects list --output json
& ".\DeploymentManager.exe" project show --project "NSP Office" --output json
& ".\DeploymentManager.exe" validate --project "NSP Office" --output json
& ".\DeploymentManager.exe" publish --project "NSP Office" --output json
& ".\DeploymentManager.exe" deploy --project "NSP Office" --mode full --non-interactive --output json
& ".\DeploymentManager.exe" deploy --project "NSP Office" --mode fast --non-interactive --output json
& ".\DeploymentManager.exe" deploy --project "NSP Office" --mode full --non-interactive --dry-run --output json
& ".\DeploymentManager.exe" status --project "NSP Office" --output json
& ".\DeploymentManager.exe" health-check --project "NSP Office" --output json
& ".\DeploymentManager.exe" backups list --project "NSP Office" --output json
& ".\DeploymentManager.exe" rollback --project "NSP Office" --to latest --non-interactive --output json
```

Use `--result-file "C:\Temp\deploy-result.json"` to write the final JSON to a file. Operational messages go to stderr. With `--output json`, stdout contains one final JSON document.

Actual deploy and rollback require `--non-interactive` and an elevated PowerShell. Dry-run performs no build, backup, IIS action, or target-file change. In a non-elevated shell it can complete inspection but reports `readyForActualDeploy: false`.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Successful |
| 1 | General failure |
| 2 | Invalid input or settings |
| 3 | Build/publish failed |
| 4 | Backup failed |
| 5 | Deploy or standalone health/rollback failed |
| 6 | Health check failed; automatic rollback succeeded |
| 7 | Health check and rollback both failed |
| 8 | Administrator access missing |
| 9 | Project, backup, or IIS site not found |
| 10 | Another operation is running for the project |

## Successful JSON example

```json
{
  "success": true,
  "operation": "deploy",
  "project": "NSP Office",
  "exitCode": 0,
  "warnings": [],
  "errors": [],
  "data": {
    "mode": "full",
    "targetPath": "C:\\inetpub\\wwwroot",
    "backupPath": "D:\\NSPBackups\\NSP Office\\20260909-120000-76.0.0",
    "previousVersion": "76.0.0",
    "deployedVersion": "77.0.0",
    "healthCheck": { "url": "http://localhost/api/v1/health", "statusCode": 200, "passed": true },
    "rolledBack": false
  }
}
```

## Failure JSON example

```json
{
  "success": false,
  "operation": "validate",
  "project": "NSP Office",
  "exitCode": 8,
  "warnings": [],
  "errors": ["Administrator access is required."],
  "data": {}
}
```

## Safety behavior

- A global named mutex prevents concurrent operations for the same project.
- Build/publish completes in staging before IIS files change.
- `web.config`, the main API DLL, and frontend output are validated.
- Backup file count and total bytes are verified.
- Configured production appsettings files are copied into staging before activation.
- The shared engine stops the IIS site/application pool before file activation.
- Health check uses timeout and retry; failed health triggers rollback unless `--no-auto-rollback` is supplied.
- Logs and JSON are redacted for password/token/connection-string labels.
