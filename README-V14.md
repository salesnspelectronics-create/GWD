# Generic Web Deployment Manager V14

V14 is a stabilization release focused on end-to-end deployment reliability.

## Changes
- Requires/elevates to Windows Administrator at startup.
- Environment Scan starts automatically when the page opens; the button is now Re-scan.
- Default reusable installer cache is `Desktop\WebDeployment-Installers`.
- Installer cache path persists in HKCU `Software\NSP\DeploymentManager` and ProgramData settings.
- Database creation is split into safe phases: create in `master`, wait for ONLINE, reconnect to the new database, then create IIS AppPool principal.
- Failed deployment produces a persistent `DEPLOYMENT UNSUCCESSFUL` state and Retry Deploy button.
- Automatic sanitized diagnostic error report is saved under `C:\ProgramData\NSP\DeploymentManager\ErrorReports`.
- After a failure, View Error Report opens the full diagnostic report and can create a ZIP diagnostic dump containing the report, full session log, deployment state, and manifest.
- Failed deployment folder is cleaned automatically; persistent Data/Logs/backups/database are never deleted by this cleanup.
- If a previous IIS site existed, V14 attempts to restore its previous physical path on failure.

## Build
Run `build-release.cmd` as Administrator or open `DeploymentManager.sln` in Visual Studio.


V14.1: Full Log window adds 'Dump current log to Desktop'.

V14.17: Start Over offers Clear Log or Keep Log. Clear Log rotates to a new uniquely named session log while retaining the prior disk log; Keep Log continues the current session.

## V14.21 — Project Lifetime / Deployment Analytics

A persistent **Deployment History** view records and summarizes every deployment attempt by project. History is stored at `C:\ProgramData\GenericWebDeploymentManager\History\` and is intentionally separate from Smart Deploy state.
