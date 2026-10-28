# V69 — Profiles, Clean Source Export, IIS/SQL Reliability

- Added deployment profiles on Project Source: Save/Load and Source Lock/Unlock. Secrets are deliberately excluded from profile JSON.
- Added Clean Source ZIP export. Default exclusions: `.git`, `IIS-Publish`, `bin`, `obj`, `node_modules`; original source is never modified.
- Hardened IIS site/app-pool refresh against disposed/rebuilt DataGridView controls and duplicate async refresh races.
- Added safe SQL connectivity fallback: when Integrated Authentication specifically fails due to an untrusted-domain context, configured SQL Authentication can be tested without logging credentials.
- Preserved V68 unified SQL Server + Database step, background auto-verification, auto-verified sidebar states, and reduced 8-step wizard flow.
- Version advanced to 69.0.0.
