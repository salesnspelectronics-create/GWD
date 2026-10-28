# V48 Test Report

This report contains only checks actually executed for the delivered package.

## Executed successfully

- `dotnet restore --ignore-failed-sources` using the local NuGet package cache: succeeded.
- `dotnet build -c Release --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build`: exited successfully; the source tree contains no automated test project, so no test cases are claimed.
- Real Node child-process test through the release `CommandRunner`: exit code 0.
- Adaptive memory policy remained active: `NODE_OPTIONS=--max-old-space-size=2430` on the build machine.
- Release executable file version confirmed as `48.0.0.0`.
- Static release verification confirmed the deployed-state comparison uses the state captured by the same publish operation, with fallback only when no captured state exists.
- Static release verification confirmed Single Folder rollback stops IIS before replacement and performs three bounded hosted-folder removal attempts.

## Not executed

- No deployment was run against the user's NSP source, production IIS website, SQL Server, or remote server.
- A destructive rollback was not simulated against the user's `C:\inetpub\wwwroot`.
