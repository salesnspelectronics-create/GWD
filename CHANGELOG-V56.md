# Generic Web Deployment Manager V56

## Release output reliability

- Added `resolve-release-root.ps1` as the single resolver for the Manager release/output folder.
- `build-release.cmd`, `build-release-full.cmd`, and `publish-latest.ps1` now all resolve the same `ManagerReleaseRoot` saved by the GUI Settings page.
- The resolver honors `GWDM_APPDATA_ROOT` and `GWDM_MANAGER_ROOT`, validates that the destination is writable, and falls back to the source-local `release-output` folder only when no configured path exists.
- A successful GUI release writes both:
  - `DeploymentManager.exe` (latest stable executable)
  - `DeploymentManager-V56.exe` (versioned executable)
  directly into the configured Manager release/output folder.
- `publish-latest.ps1` now publishes the GUI project instead of the CLI project before creating the latest/versioned GUI executables.
- Root EXEs are not replaced if publish fails or the expected published GUI executable is missing.
