# V41 Test Report

Test date: 2026-09-03  
Environment: Windows, .NET SDK 9.0.315

## Actually executed

1. `dotnet restore DeploymentManager.sln --packages C:\Users\Ali\.nuget\packages --ignore-failed-sources`
   - Result: PASS.
   - The existing local NuGet cache supplied the required packages.

2. `dotnet build DeploymentManager.sln -c Release --no-restore -p:RestorePackagesPath=C:\Users\Ali\.nuget\packages`
   - Result: PASS.
   - Warnings: 0.
   - Errors: 0.

3. Stable workspace initial-sync integration exercise, invoked against the compiled V41 assembly with a temporary source fixture.
   - Result: PASS.
   - Added: 3; Updated: 0; Unchanged: 0; Removed: 0.

4. Stable workspace delta-sync integration exercise after changing one source file, deleting one source file, and placing sentinel files in workspace `bin`, `obj`, and `node_modules` folders.
   - Result: PASS.
   - Added: 0; Updated: 1; Unchanged: 1; Removed: 1.
   - Changed source content was replaced.
   - Removed source file no longer existed in the workspace.
   - `bin` sentinel preserved: PASS.
   - `obj` sentinel preserved: PASS.
   - `node_modules` sentinel preserved: PASS.

5. `dotnet test DeploymentManager.sln -c Release --no-build --no-restore -p:RestorePackagesPath=C:\Users\Ali\.nuget\packages --verbosity normal`
   - Result: command completed successfully (exit code 0).
   - Build warnings: 0; errors: 0.
   - The Solution contains no automated test project, so no unit/integration test cases were discovered or executed.

## Not executed

- No customer application source snapshot pair was supplied for measuring real before/after Fast Local Test duration.
- No live IIS/remote/SQL environment was used.
- The WinForms UI compiled but was not manually clicked through in an interactive desktop session.

Only results actually observed are claimed.
