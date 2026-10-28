# V14.22 — Remote IIS Deployment + Shared Deployment Repository

- Added Local Server / Remote IIS Server target mode.
- Remote target fields: host/IP, WinRM port, SSL, username, session-only password.
- Added Remote Readiness + Bootstrap flow using PowerShell Remoting / WinRM.
- Remote bootstrap can enable IIS and download missing ASP.NET Hosting Bundle locally into the shared repository, transfer it to the remote server, and install it there.
- Added remote publish transfer, remote IIS AppPool/Site creation/update, backup-before-replace and external health check.
- Remote password is not persisted in profile/settings/history.
- Replaced source-adjacent `.deployment-offline` package placement with a user-selected reusable Deployment Repository.
- Repository layout includes `Installers/` and `OfflinePackages/<Application>/`.
- Offline dependencies are no longer intentionally written beside the source/site.
- Existing local deployment pipeline remains the default and is preserved.

Runtime note: source/static checks performed in authoring environment; Windows WinRM/IIS runtime verification is required on real target machines.
