# CHANGELOG V57

## Scope
PES-1 adoption plus UX/dependency-flow cleanup requested after V56.

## Changes
- Adopted Project Evolution Standard (PES-1) for this project. The release package now includes the aligned `Project-Evolution-V57.json`.
- Source preflight is now automatic whenever a valid Source folder is selected or changed.
- Removed manual Source preflight checkboxes and Run Tests / Run Again controls.
- Removed duplicated disabled Build Mode and Dependency Mode selectors from the Target page.
- Target page now focuses on deployment target, Shared Dependency Repository, Stable Build Workspace, dependency repository actions/status, and source/publish paths.
- Clarified that the selected Source is an incoming/versioned source only; dependency packages remain in the persistent Shared Dependency Repository.
- Stable Build Workspace remains persistent and project-specific subfolders are created by `DeploymentService.PrepareStableWorkspace`.
- No independent `Dependency Sync Source Folder` is introduced or required.
- Fast/full release scripts still launch the newly built Deployment Manager after a successful release, but now exit immediately afterward so the Command Prompt does not remain open.
- Failure paths still pause so the operator can read the error.
- Version advanced to 57.0.0.

## Compatibility / safety
- No database schema or production data behavior changed.
- Existing Settings paths remain valid.
- Project profile fields used by older releases are preserved for backward compatibility even if some UI controls are no longer shown.
