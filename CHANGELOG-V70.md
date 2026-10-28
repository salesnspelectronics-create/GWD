# V70 — Startup Failure Diagnostics & Layered Health Check

- Added layered post-deploy health verification: IIS AppPool, IIS Site, HTTP reachability, then application endpoint.
- Added bounded warm-up retries with explicit failure classifications.
- Detects ASP.NET Core HTTP 500.30 as `ASP.NET_CORE_STARTUP_FAILURE_500_30` instead of a generic health-check failure.
- Captures startup diagnostics before rollback: IIS state, installed .NET runtimes, redacted web.config, recent stdout/application logs, and recent Windows Application Event Log entries.
- Keeps rollback protection for persistent startup failures such as HTTP 500.30.
- A missing configured health endpoint (404) no longer forces rollback when the application root is independently reachable.
- Deployment failure popup now reports the classified startup failure and confirms diagnostics were captured before rollback.
- Preserves V69 profiles/source lock, clean-source export, IIS grid hardening, SQL authentication handling, auto verification and unified SQL/Database flow.
- Deployment Manager remains on .NET 9; version advanced to 70.0.0.
