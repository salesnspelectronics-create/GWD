# V49 Test Report

This report contains only checks actually executed for the delivered package.

## Executed successfully

- `dotnet restore --ignore-failed-sources` using the local NuGet package cache: succeeded.
- `dotnet build -c Release --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build`: exited successfully; this source tree contains no automated test project, so no test cases are claimed.
- Static Web Assets error classifier: accepted a representative `Microsoft.NET.Sdk.StaticWebAssets / No file exists for the asset` failure and rejected an unrelated compile error.
- Targeted repair test on a synthetic Workspace: removed 1 generated `staticwebassets` metadata file while preserving the source file and a `node_modules` sentinel file.
- Real Node child-process test through the release `CommandRunner`: exit code 0; adaptive `NODE_OPTIONS=--max-old-space-size=2430` remained active on the build machine.
- Release executable file version confirmed as `49.0.0.0`.
- Release source scan found no stale V48 version identifiers in active source/build metadata.
- Source inspection confirmed page controls are disposed on every page replacement and Start Over resets cancellation sources, performance/deploy state, and the in-memory log buffer.

## Not executed

- No deployment was run against the user's NSP source, IIS website, SQL Server, or remote server.
- The exact failing NSP publish was not available in this workspace, so the complete first-failure/retry-success path was not executed against that project.
- A long-running interactive UI memory soak test was not performed; memory improvements are based on fixing the identified retained-control/resource paths and successful compilation.
