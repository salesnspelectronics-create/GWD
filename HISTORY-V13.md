# Generic Web Deployment Manager V13 (.NET 9)

V13 focuses on SQL Server recovery/diagnostics and persistent npm package caching while retaining the V12/V12.1 build and logging fixes.

## SQL Server V13
- Separates SQL instance detection from actual connection verification.
- Adds a diagnostics panel for the selected instance:
  - SQL service state
  - TCP/IP state
  - Windows connection result
  - whether the current Windows account is SQL `sysadmin`
  - authentication mode
  - `sa` enabled state
  - SQL Browser status/details
- Adds **Repair local SQL connectivity**:
  - enables SQL TCP/IP in the instance registry when needed
  - starts SQL Browser for named-instance discovery when available
  - starts/restarts the selected SQL service when required
  - verifies a Windows connection after repair
- Keeps **Configure / Reset sa** for the normal case where Windows Authentication already has SQL `sysadmin`.
- Adds **Emergency recover SQL access** for a local instance when SQL `sysadmin` access was lost. The manager is already elevated by manifest; recovery temporarily restarts the selected SQL service in restricted single-user mode, grants the current Windows account SQL `sysadmin`, enables Mixed Mode and `sa`, applies the selected password, returns SQL Server to normal multi-user mode, then verifies both Windows sysadmin and `sa` login.
- SQL Agent is stopped/restored during emergency recovery so it cannot consume the single-user connection.
- Error 26 (instance not located) is classified separately instead of being reported only as a generic connection failure.

> Close SSMS, Visual Studio database tools, and other SQL clients before using Emergency recovery. Emergency recovery is intentionally limited to a SQL Server instance on the local machine.

## npm cache V13
- Persistent npm download cache is stored under `%PROGRAMDATA%\NSP\DeploymentManager\Cache\npm`.
- Dependency repair uses `--prefer-offline`, so previously downloaded Ant Design/React/Vite/TypeScript packages are reused across new source folders and Deployment Manager versions when the exact package version is already cached.
- Healthy `node_modules` trees still skip `npm install` entirely.

## Retained behavior
- V12.1 C# string/newline correction.
- Automatic frontend dependency repair and detailed npm output.
- Bounded disk-first central logging and incremental Full Log viewer.
- Status-only non-clickable sidebar.
- Build modes: Automatic, Visual Studio prepared, Ready publish folder.
- Persistent state under `%PROGRAMDATA%\NSP\DeploymentManager`.
