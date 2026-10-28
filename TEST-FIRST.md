# V14.17 first test sequence

1. Run `build-release.cmd` to produce the EXE.
2. Start `dist\GenericWeb.DeploymentManager.exe`. V14 must elevate to Administrator automatically; accept the UAC prompt.
3. Select Target & Source.
4. When Environment Scan opens, scanning starts automatically. Use **Re-scan environment** only if you want to repeat it.
5. Verify IIS / Hosting Bundle / .NET / Node / SQL cards.
6. On Installers & Runtimes, the default reusable cache is `Desktop\WebDeployment-Installers`; the selected path is persisted.
7. Verify/reset SQL as needed.
8. Enter the Database name. V14 verifies server access here; the database is created automatically during Deploy if missing.
9. Deploy. If any stage fails, the final page must display `DEPLOYMENT UNSUCCESSFUL`, a diagnostic report is saved under ProgramData, and **View Error Report** becomes available. Open it and verify **Dump Error Report** creates a ZIP diagnostic bundle on Desktop.
10. Verify **Start Over — Keep Log** resets wizard state but keeps the visible/current session log.
11. Verify **Start Over — Clear Log** resets wizard state, clears the visible log, starts a new uniquely named session log, and leaves the previous disk log intact.
12. On success, the final state must be `DEPLOYMENT SUCCESSFUL` and the health-check URL is displayed.
