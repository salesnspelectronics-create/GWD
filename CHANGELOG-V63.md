# V63 — Background Health Monitor & IIS Management

- IIS page now inventories all IIS Sites and Application Pools.
- Site controls: refresh, start, stop, explicit remove/cleanup with confirmations.
- App Pool controls: start, stop, recycle, delete only when unused; DefaultAppPool protected.
- IIS inventory shows last checked time.
- Added asynchronous BackgroundHealthMonitor: initial check after startup, then every 10 minutes.
- Background checks: IIS availability/inventory, configured SQL administrative connectivity, localhost HTTP health.
- Footer shows latest health status and check time.
- Recent successful SQL background check (10-minute validity) can satisfy the SQL wizard verification gate; connection/auth changes invalidate it.
- Background monitor performs read/check operations only; no automatic destructive remediation.
