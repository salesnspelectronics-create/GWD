# V14.25 — Compact Target UI / WinRM UX

Focused UI/UX refinement on top of V14.24.

## Changes
- Fixed initial Local Server state: Remote user, Remote password, WinRM port, SSL and Test/Bootstrap controls are disabled immediately when the page first opens in Local Server mode.
- Local Target Host is also read-only/disabled and set to the current machine name; switching to Remote IIS Server enables it.
- Target/Source page layout is more compact:
  - Application name, IIS site name and Database name are now on one Identity row.
  - Target mode and Host/IP share one row.
  - Remote user/password/WinRM port/SSL/Test share one row.
- Deployment Repository and Source Folder use full-width path fields with Browse buttons pinned at the far right.
- Ready Publish Folder uses the same full-width path picker layout.
- Wizard starts maximized so lower-page content is visible on typical desktop resolutions.
- Welcome text now correctly describes the shared Deployment Repository instead of the old beside-the-app cache behavior.

## Preserved
- V14.24 compile fix.
- Remote IIS readiness/bootstrap/deploy pipeline.
- Shared deployment repository/offline package behavior.
- Existing deployment modes, SQL, IIS, health checks, rollback and deployment history.
