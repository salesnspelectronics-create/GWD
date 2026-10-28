# V90 — N9: Bounded external rollback commands

- Added explicit 60-second per-command timeout for three appcmd recovery operations in CleanupFailedDeploymentAsync.
- Preserved uncancelled filesystem recovery swaps to avoid half-completed rollback.
- This does not impose a global rollback deadline or prove IIS recovery success.
- .NET build and Windows runtime tests: NOT RUN.
