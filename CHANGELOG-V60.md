# Generic Web Deployment Manager V60

- Fixed frontend discovery regression observed in V59.
- Generated `IIS-Publish` and `publish` trees are excluded from frontend candidates.
- Frontend candidates are source-aware and prefer a complete Vite root (`build` script + `index.html` + `vite.config.*`).
- Source preflight now reports the complete Vite frontend root.
- Drive-independent: configured D:, G:, or other workspace roots remain valid.
- PES-5 Project Evolution aligned to V60.

Runtime retest on Windows/NSP Office is pending.
