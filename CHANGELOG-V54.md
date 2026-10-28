# Generic Web Deployment Manager V54

## Scope
- Smart context-aware Browse history with recursive parent fallback when a previous version folder was deleted.
- Separate browse memory for source, publish, installers, repository, workspace, hosting and release output.
- SQL password is no longer hard-coded or persisted; a cryptographically generated session suggestion is shown and remains editable/copyable.
- New Settings dialog for Deployment Manager release/output folder, default project folder, repository and stable workspace.
- Build/release scripts source version from `Directory.Build.props` and no longer hard-code `G:\ChatGPT\DeploymentManager`.
- Safe optional Source Preflight tests on the first page; no build/deploy is triggered by them.
- Unified Diagnostics & Deployment History Center with per-tab export and Export All.
- Internal diagnostics journal records previously silent handled exceptions without logging passwords/secrets.
- Atomic `settings.json`, state and project-profile writes.
