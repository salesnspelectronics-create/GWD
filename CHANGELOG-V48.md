# Generic Web Deployment Manager V48

## Workspace-consistent post-deploy verification

- Fixed a false `STALE FRONTEND DETECTED` result after successful Full IIS Deploy.
- Post-deploy verification now compares the activated manifest with the exact fingerprint state produced by the synchronized Stable Workspace during the same deployment.
- The original version-wrapped incoming folder is no longer incorrectly compared against the Workspace-built output.
- Activated `wwwroot` is still independently hashed and compared with the staged frontend-output hash.

## Safe Single Folder rollback

- Stops the IIS site and application pool before replacing a failed hosted folder.
- Retries removal of the failed hosted folder when Windows briefly retains file handles.
- Verifies that the destination is absent before moving the rollback folder into place.
- Prevents the previous `Cannot create ... because a file or directory with the same name already exists` cleanup failure.

## Preserved

- V47 adaptive Node memory limit (1536-4096 MB).
- Stable Workspace synchronization and targeted esbuild repair.
- Online, Offline Only, Hybrid, all build/deploy modes, history, diagnostics, cancellation, remote validation, health check, and rollback.

