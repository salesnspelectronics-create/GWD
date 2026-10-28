# V43 Test Report

Only tests actually executed are reported.

## Build validation

- `dotnet restore DeploymentManager.sln` using the existing local NuGet package cache.
- `dotnet build DeploymentManager.sln -c Release --no-restore`.
- `dotnet test DeploymentManager.sln -c Release --no-build --no-restore` (the solution has no automated test project).
- Framework-dependent Windows x64 single-file Release publish.

## Focused checks

- IPv4 segmented-control parsing/clamping was exercised for valid, oversized, incomplete and non-numeric input.
- Source-root detection was exercised against the available NSP Office V1.11.64 package layout.
- Published EXE file/product version was inspected.
- ZIP contents were checked for full source, V43 changelog/test report and Release executable.

## Not executed

- Real Remote WinRM login/bootstrap: no remote server credential was supplied.
- Real IIS/database deployment, health check or rollback.
- Forced production crash; crash registration and report-writing paths were build/inspection validated only.
