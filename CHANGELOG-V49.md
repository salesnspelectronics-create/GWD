# Generic Web Deployment Manager V49

## Static Web Assets self-recovery

- Detects the ASP.NET publish failure `No file exists for the asset` from `Microsoft.NET.Sdk.StaticWebAssets`.
- Removes only generated `staticwebassets` metadata beneath Workspace `obj` folders.
- Preserves source files, dependency caches, `node_modules`, and unrelated incremental build output.
- Clears the incomplete staging output and retries `dotnet publish` exactly once.
- Reports the repair and retry explicitly in the deployment log.

## Start Over memory cleanup

- Wizard page controls are now disposed when navigating; they are no longer merely detached and retained in memory.
- Diagnostics, Deployment History, Build Performance, and Error Report dialogs are explicitly disposed after closing.
- Start Over cancels and disposes previous deployment/automation cancellation sources.
- Clears the performance snapshot, captured deploy verification state, visible log buffer, stale UI references, and page controls.
- Both Start Over choices clear the in-memory log. “Keep Log” preserves the complete disk log and continues its disk session.
- Requests an optimized garbage collection after the explicit Start Over cleanup.
- Existing UI log limits remain active: bounded line history and bounded visible text, with the complete log stored on disk.

## Preserved

- V48 Workspace-consistent verification and safe rollback.
- Adaptive Node memory protection, Stable Workspace, esbuild repair, all dependency/build modes, diagnostics, history, remote validation, cancellation, health check, and rollback.

