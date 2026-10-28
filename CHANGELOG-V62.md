# V62 — Collision-safe Backup / Rollback

- Replaced fixed `<hostingRoot>.__rollback` with a unique per-deployment backup under `<DeploymentRoot>\Backups\<Application>\<timestamp-guid>`.
- Backup is created and minimally validated before IIS is stopped; backup creation failure therefore leaves the running site untouched.
- Single-folder activation now copies the existing hosted tree to the unique backup, stops IIS, deterministically replaces the target, and restores from that exact backup on activation failure.
- Successful deployment retains rollback history and prunes older backups, keeping the newest five.
- Existing stale `wwwroot.__rollback` folders no longer collide with new deployments.
- Prepared IIS Publish behavior from V61 remains intact.
