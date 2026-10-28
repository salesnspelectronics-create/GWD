# V46 Test Report

Only executed checks are reported.

## Passed

- Both supplied V45 Diagnostic ZIPs were opened and inspected.
- Confirmed actual failure signature: `esbuild@0.25.12`, `@esbuild/win32-x64/esbuild.exe`, `EFTYPE`.
- Confirmed V45 Full IIS npm working directory was the Desktop incoming source, not the stable workspace.
- Local-cache `dotnet restore` succeeded.
- Release rebuild succeeded with zero warnings and zero errors.
- `dotnet test` exited successfully; the solution contains no automated test project.
- Native-binary classifier was checked against the captured EFTYPE signature.
- Source-path inspection confirmed stable workspace synchronization now precedes all source-based modes.
- Release EXE version and Full Source ZIP contents inspected.

## Not executed

- The user's V1.11.66 npm install/build was not executed, so its source or workspace was not mutated by testing.
- Live IIS activation/rollback, database operations and remote WinRM were not executed.
