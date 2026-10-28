# V64 Test Report

## Static verification
- Version centralized at 64.0.0: PASS.
- Footer exposes separate IIS / SQL Server / HTTP indicators: PASS by source inspection.
- Manual SQL success publishes to BackgroundHealthMonitor shared state: PASS by source inspection.
- Automatic SQL success publishes to BackgroundHealthMonitor shared state: PASS by source inspection.
- Start Over no longer adds another health-monitor event subscription: PASS by source inspection.

## Build / runtime
- .NET SDK is not available in this execution environment. Compile/publish was not run.
- Windows/IIS/SQL runtime acceptance remains PENDING.
