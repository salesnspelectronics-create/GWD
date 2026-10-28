# Generic Web Deployment Manager V47

## Adaptive Node memory guard

- Removed V46's fixed `--max-old-space-size=8192` policy, which could place severe memory/commit pressure on Windows and other running applications.
- Node and npm processes now receive an adaptive heap ceiling derived from available runtime memory.
- The automatically selected heap is constrained to 1536-4096 MB, preserving memory for Windows, IIS, browsers, SQL Server, and dotnet.
- Added the optional `GWDM_NODE_HEAP_MB` environment override. Its value is still safely constrained to 1536-4096 MB.
- The selected heap limit is written to the deployment log for diagnosis.
- Updated Node out-of-memory messages so they accurately describe the adaptive policy.

## Preserved from V46

- Stable Workspace synchronization for every source-based build mode.
- Targeted repair of invalid esbuild native binaries.
- Online, Offline Only, and Hybrid dependency behavior.
- Fast Local Test, Fast IIS Update, and Full IIS Deploy.
- Change detection, deployment history/stage timing, diagnostic bundle, remote validation, cancellation, auto deploy, health check, and rollback.

