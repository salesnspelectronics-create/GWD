# V64 — Health Indicators & Shared SQL Health State

- Replaced ambiguous footer health text with explicit status indicators: ✓ healthy, ✕ failed, ○ unknown.
- IIS, SQL Server and HTTP health are displayed independently with Last Checked time.
- A successful manual or automatic SQL administrative connection test immediately updates the shared background health state.
- SQL wizard verification and footer health no longer diverge (Verified in page while footer shows SQL —).
- Removed duplicate BackgroundHealthMonitor Updated event subscription during Start Over, preventing repeated UI callbacks over long sessions.
- Preserved the 10-minute background health cycle and V63 IIS management features.
