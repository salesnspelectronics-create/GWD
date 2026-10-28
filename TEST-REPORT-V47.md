# V47 Test Report

This file records only tests actually executed for the delivered package.

## Executed successfully

- `dotnet restore --ignore-failed-sources` using the existing local NuGet package cache: succeeded.
- `dotnet build -c Release --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test -c Release --no-build`: exited successfully; this source tree contains no automated test project, so no test cases were discovered or claimed.
- Real child-process environment test through `CommandRunner` using Node.js: succeeded with exit code 0.
- The Node child received `NODE_OPTIONS=--max-old-space-size=2430` on the build machine, confirming adaptive selection instead of V46's fixed 8192 MB setting.
- Release executable file version: confirmed as `47.0.0.0`.

## Not executed

- No deployment was performed against the user's NSP source, IIS, SQL Server, or a remote server.
- The exact Windows low-memory condition shown in the screenshots was not deliberately recreated because doing so could destabilize the machine.
