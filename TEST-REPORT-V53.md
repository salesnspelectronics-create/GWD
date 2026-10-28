# V53 Test Report

This file records only commands actually executed for V53. No real IIS deployment was performed.

## Executed

- `dotnet build src\DeploymentManager\DeploymentManager.csproj -c Release --no-restore -p:NuGetAudit=false`
  - Result: PASS
  - Warnings: 0
  - Errors: 0
  - Elapsed reported by .NET: 00:00:01.65

- `dotnet publish` for Windows x64, framework-dependent single-file GUI
  - Result: PASS

No Smoke Test and no real deployment were run.
