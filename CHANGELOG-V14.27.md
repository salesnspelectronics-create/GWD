# CHANGELOG V14.27 — Fast Self Build Fix

## Scope
This release fixes the build script used to build Generic Web Deployment Manager itself.

## Changes
- `build-release.cmd` is now the fast/default self-build path.
- Uses a persistent NuGet cache under `%LOCALAPPDATA%\GenericWebDeploymentManager\SelfBuildCache`.
- Restore is skipped when `project.assets.json` exists and the `.csproj` has not changed since the last successful restore.
- Release publish always runs with `--no-restore`, preventing a second implicit restore.
- Explicit stage output (`RESTORE`, `PUBLISH`, `DONE`) makes stalls visible.
- Added `build-release-full.cmd` for troubleshooting/full clean + forced restore.
- Existing application/deployment functionality from V14.26 is unchanged.

## Expected behavior
The first build can still take longer because packages may need to be downloaded. Subsequent builds should reuse the persistent cache and normally skip restore unless the project dependencies changed.
