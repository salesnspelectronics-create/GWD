# V38 Test Report

Date: 2026-09-02

## Executed checks

### Restore

- Command: `dotnet restore DeploymentManager.sln -p:NuGetAudit=false`
- Result: PASSED.
- `Microsoft.Data.SqlClient` was restored successfully.

### Release compilation

- Command: `dotnet build DeploymentManager.sln -c Release --no-restore`
- Result: PASSED.
- Target: `net9.0-windows`.
- Compiler result: 0 warnings, 0 errors.

### Static checks

- V38 product/file/assembly version markers: checked.
- Persistent dependency-ready state and safe fallback for V37 manifests: checked.
- Dependency-validation, dependency-repair, frontend-build, and backend-publish skip paths: checked.
- Stale-UI output hashing and successful-only persistent-state commit path: checked.
- History schema v2, full performance snapshot persistence, and expandable DataGrid stage rendering: checked.
- V37 remote validation, five-second confirmation, Offline Only default, Auto Deploy, cancellation, Build Performance, and history code paths: checked.

### ZIP integrity

- Performed after final packaging by opening the archive and testing every entry.
- The final SHA-256 is reported alongside the delivered ZIP.

## Runtime scope

- The application was compiled on Windows with the .NET 9 SDK.
- The interactive WinForms wizard and a real IIS/SQL/remote deployment were not launched or executed because those operations change the host/server environment and require deployment-specific inputs and administrator-side effects.
- Therefore this report does not claim end-to-end runtime, IIS activation, SQL migration, remote WinRM, or measured Fast Build timing results.
