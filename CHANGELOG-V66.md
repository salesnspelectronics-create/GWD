# V66 — Unified IIS & Backup Validation

- Merged the separate **IIS Site** wizard step into the main **IIS** page; wizard is now 9 steps.
- Target IIS configuration (hosted folder, site, app pool, port, host header, health path) is shown on the IIS page.
- Preserved IIS site/app-pool inventory and management actions.
- Backup validation now compares source/backup file inventories and logs counts/missing paths instead of failing merely because a source web root is empty.
- Fail-safe remains: backup validation happens before IIS is stopped or hosted files are changed.
- V65 lock-aware retry, unique backups, Prepared IIS Publish, background health monitor and SQL/IIS/HTTP status behavior are retained.

Runtime/build acceptance: PENDING (Windows/.NET runtime not available in this environment).
