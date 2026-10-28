# V42 Test Report

Only tests actually executed are listed here.

## Passed

- `dotnet restore DeploymentManager.sln --packages C:\Users\Ali\.nuget\packages --ignore-failed-sources`
- `dotnet build DeploymentManager.sln -c Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test DeploymentManager.sln -c Release --no-build --no-restore`: exited successfully; the solution contains no automated test project, so no test cases were discovered.
- Source-root resolution against the supplied V1.11.64 layout:
  - selected: `...\NSPOffice-V1.11.64-Source`
  - detected API: `src\NSPOffice.Api\NSPOffice.Api.csproj`
  - resolved synchronization root contains `src` directly.
- Source inspection confirmed that the supplied V1.11.64 frontend source declares `V64` and package version `1.11.64`.

## Not executed

- Remote WinRM authentication/bootstrap (no remote test server or credential was provided).
- IIS deployment, database mutation, rollback, and health check.
- Interactive UI automation.
