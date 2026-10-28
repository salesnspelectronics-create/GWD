# V76 — Activated Release Integrity Gate

- Fail closed if activated release manifest is missing or invalid.
- Verify activated wwwroot SHA256 against the staged release manifest.
- Compare release manifest version to smart build state when available.
- No real deployment, production database changes or migrations.

Limitations: This does not independently query the browser UI or API version endpoint, and Git commit equality is not yet independently verified. No .NET SDK available to build or run synthetic .NET tests.
