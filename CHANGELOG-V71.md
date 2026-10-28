# V71 — 71.0.0 / net9.0-windows

Based on the actual V70 full source ZIP (SHA-256 in evidence/V70-provenance.json).

- All SQL connection creation and string construction moved into SqlConnectionStrategy. Deployment, database preparation, health, manual/automatic verification and diagnostics share profile state. Workspace clones share the same session.
- Fingerprint covers instance, database, requested auth, username, password and credential eligibility. Verification has no time expiry; config changes and actual failures invalidate it. Cancellation and stale background results do not verify a changed config.
- Untrusted-domain/Integrated-auth failures permit one controlled attempt with entered/configured SQL credentials. Successful SQL auth remains selected for subsequent operations. No fallback for generic timeout, missing credentials or unapplied generated suggestions. Explicit SA reset/emergency recovery remain operator actions.
- Registry discovery retains edition/version metadata without a hidden Windows-auth probe. UI/logs identify actual authentication and fallback. Administrative preflight is distinguished from a real database connection.
- Application connection settings, including existing appsettings.Production.json, follow effective auth; refreshed after database preparation. SQL-auth database preparation retains the configured SQL principal instead of creating an unrelated Windows app-pool login.
- Profile passwords are excluded from serialization; global redaction covers logs, internal/crash/error reports, history, performance output and diagnostic ZIPs, including quoted/escaped secrets.
- V69 Profiles, Lock Source, Clean Export, grid fixes, auto verification and merged SQL/DB UI retained. V70 health, 500.30 diagnostics and rollback retained.
- Corrected stale release metadata; added .NET 9 SDK selection, tests and evidence.

Live target-server SQL/IIS runtime acceptance remains pending.
