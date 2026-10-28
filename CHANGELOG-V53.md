# Generic Web Deployment Manager V53

- Browse dialogs reopen the last selected folder instead of defaulting to Downloads.
- SQL administrative connection includes a Show Password toggle and wider responsive panels.
- Installers and Runtimes displays detected SQL Server versions as Already Installed and skips SQL downloads whenever an instance is detected, including SQL Server 2012.
- SQL Server 2019 Express is the fallback package only when SQL Server is actually missing; SQL Server 2022 is no longer forced as the default.
- Successful release batch builds copy the GUI to `G:\ChatGPT\DeploymentManager\DeploymentManager-V53.exe` and `DeploymentManager.exe`, then launch the GUI. This does not start an IIS deployment.
- Existing V52 GUI, headless CLI, workspace, diagnostics, dependency modes, history, rollback, and deployment behavior are preserved.
