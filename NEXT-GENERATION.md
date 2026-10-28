# Generic Web Deployment Manager — Next Generation Backlog

## Open Backlog

### NG-002 — Remote Deployment hardening / agentless transport
Status: Open after V14.22 runtime verification begins.
- Verify WinRM HTTP/HTTPS configurations across domain/workgroup servers.
- Add TrustedHosts guidance/assisted configuration where Windows requires it.
- Add remote rollback execution if health check fails after activation.
- Extend prerequisite bootstrap to optional .NET SDK / Node / SQL only when remote-build or remote-SQL modes actually require them.
- Consider BITS/SMB transport fallback for very large publish payloads.
- Add certificate thumbprint validation for WinRM HTTPS instead of skip-check mode when enterprise PKI is available.

## Completed / Release History

### NG-001 — Project Lifetime / Deployment Analytics
Status: Implemented in V14.21.

### V14.22 foundation — Remote IIS + Shared Repository
Status: Implemented candidate; runtime verification pending.
- Remote IIS target selection and readiness/bootstrap.
- Local prerequisite download + remote transfer/install for Hosting Bundle.
- Remote publish transfer and IIS activation.
- Shared reusable Deployment Repository replaces source-adjacent offline package storage.


## V14.28 Safe Fast Deploy
- COMPLETED: persistent per-project frontend/backend/dependency fingerprints outside source.
- COMPLETED: fresh frontend output overlay and local stale-UI verification.
- COMPLETED: Fast Build remains default and safe-fail on stale frontend.
- OPEN: remote IIS file-level fingerprint verification after transfer/activation.
- OPEN: optional semantic Source/API/UI version endpoint adapter when a project exposes a machine-readable version.

## V14.30 Offline / Local Dependency Repository
- COMPLETED: persistent repository path and generic repository folder structure.
- COMPLETED: Offline Only / Local First / Online Normal modes; Offline Only is the new default per operator requirement.
- COMPLETED: shared NuGet package cache and npm cache outside source/version folders.
- COMPLETED: explicit one-time online synchronization and offline readiness test.
- COMPLETED: no automatic Internet fallback in Offline Only.
- COMPLETED: top-level NuGet dependency source-status logging.
- OPEN / Phase 2 hardening: richer transitive dependency readiness report and percentage, explicit missing-package table, repository import/export.
- OPEN / Phase 3: full Repository Manager UI, provider abstraction for additional package managers, .NET SDK/runtime archive management, Build Tools/CLI tools, installer catalog, cleanup with explicit confirmation only, repository portability/export, audit-ready generated setup scripts.

## V31 completed — Auto Deploy
- Bottom-bar Auto Deploy orchestration added.
- Sequential page-by-page validation with stop-on-failure behavior implemented.
- Existing deployment engine remains the single execution path after validation.


## V32 completed — Offline Preparation / Cancel Deploy UX
- Dedicated Offline Dependencies stage with prepare + clean offline verification.
- Clean-temp npm cache population/verification to prevent false readiness from existing node_modules.
- NuGet local feed archive collection.
- Deploy button morphs to Cancel Deploy while running.
- Auto Deploy source gating and hand cursor UI rule.

## V35 completed — Fast Change
- Start Over -> Target & Source.
- Offline Only deterministic default.
- Visible/locked Sync Missing Online operation with final result.
- Auto Deploy skips recurring offline repository pre-check/preparation; actual build is authoritative.
- Missing offline dependency routes back to Offline Dependencies.
