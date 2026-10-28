# Generic Web Deployment Manager V51

- Added official `DeploymentManager.exe` console/headless application.
- GUI and CLI reference the same DeploymentService, SqlServerService, prerequisite, command, and profile models.
- Added project profile persistence without passwords or tokens.
- Added JSON-capable projects list, project show, validate, publish, deploy full/fast, dry-run, status, health-check, backups list, and rollback commands.
- Added stable exit codes 0-10, result-file output, stderr operational logging, redaction, per-project global lock, publish artifact validation, verified backups, production-settings preservation, health retries, and automatic rollback.
- Added `README-CLI.md`, a pass-through PowerShell wrapper, and version metadata.
- Fixed the V50 Start Over/page-navigation crash by detaching the reusable log TextBox before disposing review-page controls.
- Preserved the V50 GUI and all prior Workspace, dependency, Static Web Assets, memory cleanup, diagnostics, history, IIS, health, and rollback behavior.
