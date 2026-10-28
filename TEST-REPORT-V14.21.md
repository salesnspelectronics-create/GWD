# Test Report — V14.21

## Verification performed in this environment

- Verified V14.21 source is based directly on the supplied V14.20 source tree.
- Verified assembly/file/package metadata is `14.21.0` / `14.21.0.0`.
- Verified persistent history uses `C:\ProgramData\GenericWebDeploymentManager\History\` and is not stored beside the EXE.
- Verified project identity is derived from Application Name + IIS Site Name + Database Name and excludes Source Folder from the stable key.
- Verified deployment history is started before `PublishAsync` and finalized in the existing `DeployAsync` `finally` path.
- Verified Successful / Failed / Cancelled results are normalized and written without storing SQL password or other credential properties.
- Verified Smart/Full/Prepared mode data, Offline flag, session log, source, target, version, duration, and deployment interval are represented in history records.
- Verified Deployment History UI exposes project selection, lifetime summary, release/deployment timeline, per-attempt table, session-log opening, and History-folder opening.
- Verified NG-001 is removed from Open Backlog and moved to Completed / Release History.
- Performed source-level delimiter/string checks and ZIP integrity checks.

## Not runtime-tested here

This execution environment does not contain the Windows .NET 9 SDK, IIS, SQL Server, or the required Windows desktop runtime. Therefore the following are **not claimed as runtime-tested** here:

- `dotnet build` / `dotnet publish` of Deployment Manager itself
- WinForms UI rendering on Windows
- Real IIS deployment
- Real SQL Server deployment
- Real online/offline application deployment
- Persistence across an actual Windows reboot

## Required Windows acceptance test

1. Build V14.21 using `build-release.cmd`.
2. Launch as Administrator.
3. Open **Deployment History** before the first deployment and confirm the empty-history state is shown.
4. Perform one successful deployment; confirm one Successful history attempt appears with elapsed time and session log.
5. Perform a deliberately failed deployment; confirm Failed is recorded.
6. Start and cancel a deployment; confirm Cancelled is recorded.
7. Deploy the same project from a differently named/versioned source folder while keeping Application/Site/Database identity stable; confirm it remains in the same project lifetime.
8. Close and reopen Deployment Manager; confirm history remains available.
9. Confirm Smart Deploy and Offline Build still execute their existing pipelines.
10. Confirm the History JSONL files contain no SQL password or credential secrets.
