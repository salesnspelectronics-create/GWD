# V45 Test Report

Only checks actually executed are reported.

## Passed

- Local-cache `dotnet restore`.
- `dotnet build DeploymentManager.sln -c Release`: zero warnings, zero errors.
- `dotnet test`: exited successfully; the solution contains no automated test project.
- Central Node runner environment check confirmed `NODE_OPTIONS=--max-old-space-size=8192` in the child process.
- Static path inspection confirmed OOM classification precedes the Hybrid dependency fallback wrapper.
- Static path inspection confirmed IIS site/app-pool existence checks precede restart commands.
- Release EXE version and Full Source ZIP contents inspected.

## Not executed

- The user's full V1.11.66 frontend build was not rerun because the supplied package path is outside the version source and can mutate its `node_modules`/build output.
- Live IIS activation/rollback, database operations and remote WinRM were not executed.
