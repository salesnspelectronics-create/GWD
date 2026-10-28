# V35 Test Report

## Static checks performed
- V35 version metadata and UI version text updated.
- Start Over target verified in source: `ShowPage(1)`.
- Startup dependency mode verified in source: forced `Offline Only`.
- Source-change handler verified to return Dependency Mode to `Offline Only`.
- Auto Deploy Offline Dependencies stage verified to skip readiness validation/preparation.
- Offline Dependencies page entry verified to perform no automatic repository scan.
- Sync Missing Online busy-state wiring verified: navigation/Auto Deploy are blocked via `_dependencySyncRunning` and explicit UI controls are disabled during sync.
- Missing offline dependency error routing added for common npm/NuGet offline-cache errors.
- Archive integrity verified after packaging.

## Runtime limitation
A real Windows/.NET 9/WinForms/NuGet/npm/IIS build and deployment could not be executed in the current environment because the .NET SDK is not installed. Runtime verification remains required on the target Windows machine.
