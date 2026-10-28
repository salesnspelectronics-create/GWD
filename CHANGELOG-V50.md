# Generic Web Deployment Manager V50

## Fast Local Test Static Web Assets recovery

- Extends V49's Static Web Assets repair from `dotnet publish` to the incremental `dotnet build` used by Fast Local Test.
- Detects only the specific `Microsoft.NET.Sdk.StaticWebAssets` missing-asset failure.
- Recognizes that the first NSP build may synchronize a bundled prebuilt frontend after MSBuild evaluated the old `wwwroot` file list.
- Removes only generated `staticwebassets` metadata beneath Workspace `obj` folders and retries the same build exactly once.
- Preserves source files, `node_modules`, `bin`, NuGet restore state, and unrelated incremental caches.
- Emits explicit `[STATIC-ASSETS]` messages for detection, repair count, retry, and recovery success.

## Preserved

- V49 Start Over memory/resource cleanup and bounded UI logging.
- Static Web Assets recovery for Full IIS publish.
- Workspace-consistent verification, safe rollback, adaptive Node memory protection, Stable Workspace, esbuild repair, all dependency modes, diagnostics, history, remote validation, cancellation, health check, and rollback.

