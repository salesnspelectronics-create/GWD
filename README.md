# Generic Web Deployment Manager V72

Version **72.0.0**, target **net9.0-windows**. Full source based on the uploaded V71 Unified SQL baseline.

See [CHANGELOG-V72.md](CHANGELOG-V72.md), [TEST-REPORT-V72.md](TEST-REPORT-V72.md) and [Project-Evolution-V72.json](Project-Evolution-V72.json). Build with `dotnet build DeploymentManager.sln -c Release` on Windows with .NET 9 SDK. Existing release scripts remain available.

SQL verification now shares one connection strategy, with controlled configured-credential fallback and persistent effective auth for the session. Build and automated checks passed; live SQL/IIS acceptance remains pending.

---

## Historical usage documentation

# Generic Web Deployment Manager V41

A Windows WinForms deployment wizard for ASP.NET Core + React/Vite applications hosted on IIS.

V41 adds a user-selected stable smart workspace for genuinely incremental Fast Local Test builds while retaining V40 dependency flows. See `CHANGELOG-V41.md` and `TEST-REPORT-V41.md`.

## Current highlights

- Select the exact IIS hosting folder during the wizard.
- Choose Single Folder or Versioned Folders deployment layout.
- Build into staging first, then activate the hosted folder after a successful compile.
- Live deployment elapsed timer.
- 50/50 Review/Log layout and clearer current-operation footer.
- Open deployed site uses the Windows default browser.
- Minimal / Normal / Verbose UI log levels while preserving the full disk log.

Run `build-release.cmd` from an Administrator command prompt, then launch the EXE from `dist`.

## V14.12
See CHANGELOG-V14.12.md for default IIS website mode, Advanced IIS settings, activity-line cleanup, and elapsed timer updates.


## V14.17
- Start Over now offers Clear Log and Keep Log modes.
- Clear Log starts a new retained disk-log session instead of destroying prior diagnostics.
- Copy Error Report was replaced by a report viewer with ZIP diagnostic dump.
- See `CHANGELOG-V14.17.md`.

## V14.22 — Project Lifetime / Deployment Analytics

V14.22 adds persistent deployment-lifecycle analytics. Use **Deployment History** in the wizard footer to view per-project lifetime metrics, deployment/release timeline, individual deployment attempts, and original session logs. History is stored independently of the executable at `C:\ProgramData\GenericWebDeploymentManager\History\`.


## V14.22 Remote IIS / Shared Repository
Remote IIS deployment is available as a target mode. Offline/build dependency packages and prerequisite installers are stored in a user-selected reusable Deployment Repository rather than beside the site source. See CHANGELOG-V14.22.md.

## Project Evolution (PES-1)
From V57 onward, this project uses PES-1. Normal continuation/delivery is the full Source plus the aligned `Project-Evolution-V<version>.json`. Update Project Evolution in the same development cycle as the software release; do not infer runtime acceptance from source/build evidence alone.
