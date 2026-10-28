# V31 — Auto Deploy

- New bottom-bar **Auto Deploy** action.
- Auto Deploy walks the wizard from the current step through every remaining stage.
- Each stage is shown in the UI while it is checked.
- Environment scan, IIS readiness, runtime readiness, SQL administrative connection, database connection, deployment location and IIS site settings are validated automatically.
- When all pre-deployment stages pass, the final deployment starts automatically.
- If any stage requires attention, automation stops on that exact page and reports the reason; it does not skip the failed requirement.
- Manual Next/Back/Deploy workflow remains available.
- Versioning scheme changed from V14.x to sequential V31, V32, V34... per project convention.
