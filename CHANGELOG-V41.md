# V41 — Stable Smart Workspace

## Stable workspace

- Added a user-selectable `Stable Build Workspace` root to the first wizard page.
- The selection is persisted and restored; no drive or workspace path is hardcoded.
- Fast Local Test creates one stable child workspace per application.
- Incoming version folders are treated as source snapshots. Builds run from the stable workspace instead of each newly extracted version folder.

## Hash-based delta synchronization

- Every non-generated incoming source file is identified by relative path and SHA-256 content hash.
- New files are copied, changed files are replaced, unchanged files retain their workspace copy and timestamp, and files removed from the incoming source are removed from the workspace.
- A persistent `.gwd-workspace-manifest.json` records the last synchronized source set.
- Sync counts (`Added`, `Updated`, `Unchanged`, `Removed`) are logged and workspace sync appears in Build Performance.

## Preserved incremental caches

- `node_modules`, `bin`, `obj`, `.git`, `.vs`, `dist`, and `out` are excluded from incoming-source synchronization.
- Workspace copies of `node_modules`, `bin`, and `obj` survive version changes, enabling npm and MSBuild to reuse warm state.
- Dependency repair is requested only when dependency fingerprints changed or the preserved dependency tree is incomplete.
- Backend builds can reuse the stable `obj/project.assets.json` and compiler outputs; unchanged backends are skipped entirely.

## Safety and correctness

- The workspace must be outside the incoming source folder, preventing recursive synchronization.
- The detected API project must remain inside the incoming source and must exist after synchronization.
- The manifest is replaced atomically after a successful sync.
- Only source files recorded in the prior manifest can be removed; generated/cache files are never included in that removal set.
- A changed dependency fingerprint triggers frontend dependency repair even when ordinary frontend-source files did not change.

## Timing fix

- Deployment timing now stops before the success dialog is displayed.
- Time spent leaving the success dialog open is no longer recorded as deployment runtime or history elapsed time.

## Diagnostics and preserved features

- Diagnostic Bundle now includes workspace root information and workspace manifests.
- V40 Online/Offline Only/Hybrid behavior is preserved.
- Fast Local Test, Fast IIS Update, Full IIS Deploy, Deployment History, Remote Validation, cancellation, Auto Deploy, health check, rollback, and Offline Repository remain available.

## Version

- Product, assembly, and file version: `41.0.0`.
