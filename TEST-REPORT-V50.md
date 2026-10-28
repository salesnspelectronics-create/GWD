# V50 Test Report

This report contains only checks actually executed for the delivered package.

## Executed successfully

- `dotnet restore --ignore-failed-sources` using the local NuGet package cache: succeeded.
- `dotnet build -c Release --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build`: exited successfully; this source tree contains no automated test project, so no test cases are claimed.
- Static Web Assets classifier accepted the representative Fast Local Test error (`Microsoft.NET.Sdk.StaticWebAssets` plus `No file exists for the asset`) and rejected an unrelated compiler failure.
- Targeted Debug metadata repair test removed 1 generated `staticwebassets` file while preserving source and `node_modules` sentinel files.
- Read-only inspection of the user's NSP V75 Workspace confirmed that the CSS named in the failure existed after the failed build and that its project target replaces `wwwroot/assets` with bundled prebuilt files during the build. This supports the bounded retry strategy.
- Real Node child-process test through release `CommandRunner`: exit code 0; adaptive `NODE_OPTIONS=--max-old-space-size=2430` remained active on the build machine.
- Release executable file version confirmed as `50.0.0.0`.
- Release source scan found no stale V49 identifiers in active source/build metadata.

## Not executed

- The NSP Workspace and IIS deployment were inspected read-only; no build or deployment was run against the user's `D:\Developer-Packages` Workspace, IIS, SQL Server, or remote server.
- Therefore the complete first-failure/recovery-build-success sequence must be confirmed by running V50 against the user's NSP source.
