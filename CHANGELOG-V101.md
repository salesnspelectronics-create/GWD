# GWD V101 — Compatible/Strict UI version contract

- `DeploymentProfile.UiVersionContractMode` defaults to `Compatible`; set to `Strict` in the profile JSON to require `gwd-ui-version.json`.
- Compatible: missing application-owned UI version is explicitly logged `UNVERIFIED`; no claim of a matching UI version is made.
- Strict: missing version file or missing verified build evidence fails deployment.
- Both modes: a present invalid or mismatched UI version fails deployment.
- Both modes: fresh frontend output hash, source-tree provenance, HTTP release ID, activated asset hash remain mandatory.
- Manifest records `uiVersionVerified`, so post-deploy HTTP checks follow the same evidence-based policy.
- Existing recovery and deployment flow preserved.

Limitations: Windows/.NET 9 Release Build and runtime/IIS tests not executed. The setting is a persisted profile property (JSON); a wizard control has not been added. Existing unrelated API-version gate is unchanged.
