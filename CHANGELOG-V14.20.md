# Generic Web Deployment Manager V14.20

## Offline Build Auto-Prepare Fix

- Offline Build no longer immediately fails only because `.deployment-offline` is missing or not ready.
- At deployment time the existing Offline Package is validated first.
- If validation fails, Deployment Manager automatically calls the existing `PrepareOfflinePackageAsync` path to populate the project-local NuGet/npm cache, then validates it again.
- Internet access is permitted only during this automatic preparation step; the actual Offline Build remains local-only.
- If automatic preparation cannot complete (for example, the server is truly offline and no cache was supplied), deployment fails with an explicit instruction to prepare the package on an Internet-connected machine and copy the complete `.deployment-offline` folder with the source.
- No parallel deployment engine was added. Existing Smart/Full/Prepared, Backup, SQL, IIS, Health Check, Rollback, Logging and Error Report flows remain unchanged.

## UI

- Offline checkbox text now explains that the local dependency cache is automatically prepared when needed.
- Existing manual `Prepare / Validate Offline Package` remains available as an explicit preflight action.

## Deferred Feature

- Project Lifetime / Deployment Analytics is still **not implemented in the application UI**. It remains tracked in `NEXT-GENERATION.md` as NG-001.
