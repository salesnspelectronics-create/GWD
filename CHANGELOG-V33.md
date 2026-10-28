# V34 — Step Panel Compile Fix

- Fixed V32 compile failure: `_stepPanel` was referenced by `BuildShell()` but the field declaration was missing.
- Added the missing `FlowLayoutPanel _stepPanel` field.
- Updated product/version metadata to V34.
- Disabled NuGet vulnerability audit during self-build/restore to avoid unnecessary calls to `api.nuget.org` when working offline.
- Preserved V32 Offline Preparation, Auto Deploy, Cancel Deploy, Fast Build, persistent repository, and UI behavior.
