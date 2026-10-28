# V38 — Change-Aware Fast Build + History Performance

## Change-aware Fast Build

- Persistent successful build state now records whether the dependency set was validated successfully.
- When frontend and backend dependency fingerprints are unchanged and the previous successful state is dependency-ready, Offline dependency validation is skipped.
- When the dependency fingerprint is unchanged, frontend dependency repair is skipped.
- When `FrontendHash` is unchanged, frontend dependency checking and frontend compilation are skipped and the last successful `wwwroot` is reused.
- When `BackendHash` and database/initialization fingerprints are unchanged, `dotnet publish` is skipped and the last successful hosted publish is copied into staging.
- A changed or untrusted dependency state forces validation/repair again. Old V37 state files deserialize safely with `DependenciesValidated=false`, causing one safe validation before V38 trusts them.
- Persistent state is still committed only after successful activation, health verification, and stale-UI verification.
- Staged `wwwroot` continues to be hashed and stored in the state manifest for stale-UI protection.

## Deployment History performance records

- History schema upgraded to version 2 while retaining backward compatibility with version 1 JSONL records.
- Each completed deployment records the build total and every measured stage: elapsed time, status (`DONE`, `SKIPPED`, or `FAILED`), and detail.
- Failed build stages now retain their failure status and error detail in the performance trace.
- Deployment History now uses an expandable DataGrid. Clicking a deployment row expands its Build Performance stages immediately beneath that row.
- Existing deployment lifetime summary, session logs, outcomes, versions, durations, targets, and project grouping remain available.

## Preserved V37 behavior

- Remote IPv4, username, password, and port validation.
- Five-second Yes auto-confirm countdown for Default IIS website takeover.
- Standalone Build Performance window and report copy.
- Offline Only defaults, Auto Deploy, cancellation/cleanup paths, Safe Fast Deploy, persistent history, and stale-UI protection.

## Version

- Product, assembly, and file version: `38.0.0`.
- Target framework remains `net9.0-windows`.
