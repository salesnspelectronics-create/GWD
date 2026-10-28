# Generic Web Deployment Manager V52

- Verified the V50/V51 `System.Windows.Forms.TextBox` disposal root cause.
- Preserved the reusable review-page log control before disposing transient page controls.
- Added a late-callback guard so shutdown or page-reset logging cannot access a disposed control.
- Kept the official headless CLI and shared deployment services introduced in V51.
- Added `publish-latest.ps1` to build first and then refresh `G:\ChatGPT\DeploymentManager\latest`.
- Strengthened the PowerShell wrapper to capture and return the CLI exit code explicitly.
- No real IIS deployment was performed while preparing V52.
