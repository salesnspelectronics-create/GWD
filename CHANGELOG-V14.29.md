# V14.29 — Offline / Local Dependency Repository

## Implemented
- Added persistent Dependency Resolution Mode with three choices:
  - Offline Only (new default)
  - Local First
  - Online / Normal
- Added persistent Offline / Local Repository path. When no previous setting exists and drive D: is available, the default is `D:\Developer-Packages`; otherwise a ProgramData repository is used.
- Repository is outside source and outside Deployment Manager version folders and creates generic folders for NuGet, DotNet, Node, Tools, Installers, Cache, Scripts, Config, and Projects.
- NuGet shared package cache is now `<Repository>\NuGet\packages` and preserves side-by-side package versions.
- npm shared cache is now `<Repository>\Node\npm-cache`.
- Offline Only generates an isolated NuGet config with online package sources removed. It may use the shared repository and the standard system NuGet package cache, then fails without Internet fallback.
- Local First generates an isolated NuGet config ordered as shared repository/cache -> system NuGet cache -> nuget.org.
- Online / Normal remains selectable.
- Removed automatic online preparation from normal Offline Only deployment. Missing dependencies now fail explicitly and instruct the operator to run the one-time synchronization action.
- Added `Sync Missing Online Once`: temporarily allows online retrieval of missing NuGet/npm dependencies into the persistent repository, validates offline readiness, then returns the UI to Offline Only.
- Added `Test Offline Build`: performs dependency restore validation with online NuGet/npm access disabled.
- Added dependency-check logging for top-level NuGet PackageReference entries (`LOCAL`, `SYSTEM CACHE`, `ONLINE REQUIRED`, `MISSING`).
- Fast Build remains the default build mode and V14.28 fingerprint/stale-frontend protection is retained.
- Folded the previously requested Remote Host/IP/User/Password/WinRM controls into one compact target row.

## Internet policy
`Offline Only` never auto-falls back to Internet for dependency restore. Internet is used only when the operator explicitly chooses `Sync Missing Online Once`, or selects Local First / Online Normal.
