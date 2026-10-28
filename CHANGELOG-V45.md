# Generic Web Deployment Manager V45

## Node/Vite out-of-memory fix

- Every centrally launched `node`/`npm` process now receives `NODE_OPTIONS=--max-old-space-size=8192`.
- The policy applies to dependency install/repair, package lifecycle scripts, TypeScript and Vite builds.
- Node heap exhaustion is detected separately from registry/cache/network dependency failures.
- Hybrid mode no longer reports a misleading “online registry fallback failed” message when the real cause is JavaScript heap exhaustion.
- OOM errors now advise closing memory-heavy applications and retrying, while retaining full technical output in Diagnostics.

## Safe IIS cleanup/restart

- Restart checks whether the configured IIS application pool exists before recycling it.
- Restart checks whether the configured IIS site exists before starting it.
- Missing IIS objects are logged as skipped operations instead of producing secondary `Cannot find SITE object` errors during cleanup.

## Preserved

- V44 streamlined 10-step wizard, source-first mode selection and hosted-version warning.
- Stable workspace, change detection, dependency modes, diagnostics, history, crash capture, cancellation, rollback and health checks.
