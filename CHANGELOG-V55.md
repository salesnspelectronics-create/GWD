# Generic Web Deployment Manager V55

## Changes
- Source preflight tests now run automatically after a source folder is selected and on initial load when a valid remembered source exists. Button changes to **Run Again**.
- Settings and Diagnostics modal windows close with Esc.
- Removed **Default project/source folder** from Settings. Source browsing uses contextual last-used path + parent fallback + last successful source state.
- Added live CPU/RAM/private/managed memory/thread metrics to the main footer and a **CPU / Memory** tab in Diagnostics Center.
- Runtime metric history is bounded (720 samples) and is exportable independently or via Export All.
- No periodic forced `GC.Collect()` is used. UI log history remains bounded and wizard page controls are disposed between pages.
- Build and Dependency mode selectors on Deployment Settings are read-only/disabled summaries because the authoritative selection is made on Project Source.
- Renamed repository UI to **Shared Dependency Repository** and clarified that it persists across source versions and is never the selected source folder.
- Release/repository/workspace settings remain persistent.
- Existing V54 unified diagnostics, export, centralized versioning, SQL suggested random password and credential redaction are preserved.
