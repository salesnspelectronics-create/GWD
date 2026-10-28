# V44 Test Report

Only executed checks are reported.

- `dotnet restore` using the available local package cache.
- `dotnet build -c Release`: passed with zero warnings and zero errors.
- `dotnet test`: executed; no automated test project exists in the solution.
- Framework-dependent Release output was assembled from the successful Release build and includes the EXE, runtime configuration, dependencies and native runtime files.
- Wizard step-map inspection: 10 steps; no standalone Dependencies page; final Deploy action is step 10 (zero-based index 9).
- Segmented IPv4 control size and parsing checks executed.
- Published EXE version and Full Source ZIP contents inspected.

## Publish environment note

- The optional single-file `win-x64` publish was attempted but the installed SDK reported missing generated intermediate files while working under the unusually long task path. It is not reported as passed. The delivered `dist` therefore uses the complete multi-file framework-dependent Release output from the successful build.

Not executed: live IIS deployment, remote WinRM login, database mutation, rollback, and browser health verification.
