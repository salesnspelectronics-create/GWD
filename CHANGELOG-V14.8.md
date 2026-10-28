# Deployment Manager V14.8

- Added a dedicated **Deployment Location** step.
- The hosted site folder is now user-selectable and remembered between runs.
- Added deployment layouts:
  - **Single Folder**: the active site always lives in the exact selected folder.
  - **Versioned Folders**: releases are created as subfolders below the selected hosting root.
- Build/publish now occurs in a staging folder first; the IIS-hosted folder is activated only after compilation succeeds.
- Single Folder mode keeps a temporary rollback copy during activation and restores it if deployment/health-check fails.
- Optional cleanup of older release folders in Versioned mode runs only after a successful health check.
- Fixed the deployment elapsed timer so it updates live from the moment Deploy starts and freezes at the final duration.
- Review & Deploy keeps the summary/log split near 50/50.
- Reworked the bottom footer so the current-operation text no longer sits under/behind the left navigation panel.
- Enlarged Back / Next / View Logs / Copy Error Report controls.
- Current-operation text follows the selected UI log level instead of being polluted by hidden npm verbose/timing lines.
- Open deployed site / Open hosted folder remain unavailable until a deployment has completed successfully.
- Existing browser selector and Minimal / Normal / Verbose log modes are retained.
