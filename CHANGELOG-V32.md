# V32 — Offline Preparation / Deploy-Cancel UX

- Added a dedicated **Offline Dependencies** wizard stage before environment/server checks.
- Added **Prepare Complete Offline Repository** and **Test Offline Readiness** actions.
- Offline preparation now performs npm population from a clean temporary install so existing `node_modules` cannot hide missing cached packages.
- Offline npm validation also runs from a clean temporary install with `--offline`; deployment cannot continue in Offline Only until this passes.
- NuGet `.nupkg` archives found in the persistent package folders/system cache are copied into the local NuGet feed after online preparation.
- Auto Deploy validates Offline Dependencies as an explicit stage and stops there if the repository is incomplete.
- During deployment, the normal **Deploy** button becomes **Cancel Deploy** instead of showing a separate cancel button.
- Auto Deploy is disabled until a valid source (or prepared publish folder) has been selected.
- Clickable buttons/link controls use a hand cursor.
- Fixed duplicate `stagedPublish` declaration and duplicate hosted-folder click handler inherited in V31 source.
- Version scheme continues as V32.
