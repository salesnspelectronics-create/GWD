# V40 Test Report

Test date: 2026-09-02  
Environment: Windows, .NET SDK 9.0.315

## Actually executed

1. `dotnet restore DeploymentManager.sln`
   - Result: FAILED in this environment.
   - NuGet returned `NU1301` because its HTTPS connection to `https://api.nuget.org/v3/index.json` failed during SSL authentication.

2. `dotnet restore DeploymentManager.sln --packages C:\Users\Ali\.nuget\packages --ignore-failed-sources`
   - Result: PASS.
   - The required `Microsoft.Data.SqlClient 5.2.2` package was available in the existing local NuGet cache.

3. `dotnet build DeploymentManager.sln -c Release --no-restore -p:RestorePackagesPath=C:\Users\Ali\.nuget\packages`
   - Result: PASS.
   - Warnings: 0.
   - Errors: 0.
   - Output: `src/DeploymentManager/bin/Release/net9.0-windows/GenericWeb.DeploymentManager.dll`.

4. `dotnet test DeploymentManager.sln -c Release --no-build --no-restore -p:RestorePackagesPath=C:\Users\Ali\.nuget\packages --verbosity normal`
   - Result: command completed successfully (exit code 0).
   - The Solution contains no automated test project, so no unit/integration test cases were discovered or executed.

5. Source-path audit with `rg` for `--offline`, legacy dependency names, forced Offline assignments, and offline preparation/validation call sites.
   - Result: PASS after correction.
   - The generated npm `--offline` argument exists only in the centralized command selector's `Offline Only` branch. Remaining legacy names occur only in migration normalization and historical changelogs.

## Not executed

- No customer ASP.NET/Node application was supplied for a real npm registry/cache fallback test.
- No live IIS site, remote Windows host, SQL Server instance, or health endpoint was supplied. End-to-end deployment, remote validation, health check, cancellation, and rollback were not run.
- The WinForms UI compiled but was not manually clicked through in an interactive desktop session.

Only the results above are claimed.
