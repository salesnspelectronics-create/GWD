# V36 — Build Performance Trace

- Added a dedicated **Build Performance** button in the wizard footer, separate from Deployment History.
- The button becomes available after a build/publish attempt has produced timing data, including failed/cancelled attempts when timing data exists.
- Added a screenshot-friendly performance window with stage, elapsed time, percentage of build time, status, and detail.
- Added **Copy Report** so the complete timing report can be pasted directly into ChatGPT.
- Fast Build timing now measures the expensive build-path components including staging cleanup, source fingerprinting, previous-state read, hosted baseline copy, offline dependency validation, frontend dependency checks/repair, frontend build, dotnet restore, dotnet publish, frontend output copy/reuse, output hashing, final fingerprinting, and build-state writing.
- Explicitly marks skipped Fast Build stages such as frontend build / dotnet restore / dotnet publish when reuse rules skip them.
- Build pipeline total is emitted to the normal deployment log with a `[PERF]` marker.
- Existing V35 Fast Change behavior is preserved.
