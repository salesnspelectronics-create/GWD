# Generic Web Deployment Manager V14.26 — Fast Build / Persistent Cache

## Primary objective
Reduce normal deployment time and make the fastest safe path the default.

## Changes
- **Fast Build** is now the default Build Mode on first load, Start Over, Source page, and Review page.
- Fast Build compares the current source with the last successful hosted state and skips unchanged stages.
- If neither backend nor database initialization changed, the previous hosted binaries are reused.
- If only frontend changed, only frontend compilation runs; dotnet publish is skipped.
- If backend changed but frontend did not, frontend npm build is skipped and the previous successful `wwwroot` is reused after backend publish.
- Fast frontend checks avoid the expensive `npm ls` pass; missing build tools still trigger one repair/retry.
- Backend dependency files (`.csproj`, props/targets, NuGet config/lock files) are hashed separately from backend source.
- `dotnet restore` is skipped when backend dependency inputs are unchanged and `project.assets.json` is already present.
- When restore is required, NuGet packages are stored under the selected Deployment Repository and reused.
- Online npm repair/install now uses the selected Deployment Repository persistent npm cache rather than a cache tied to the executable/source folder.
- Build verbosity was reduced for the normal Fast path.

## Persistent repository layout
The selected Deployment Repository remains outside the source/version folder:
- `Installers/` — prerequisite installers
- `BuildCache/nuget-packages/` — reusable online NuGet packages
- `BuildCache/npm-cache/` — reusable online npm packages
- `OfflinePackages/<Application>/` — strict offline packages

Deleting/replacing a source checkout or an old Deployment Manager release does not delete this repository. A fresh source checkout may need a lightweight restore to recreate local metadata/node_modules, but downloaded package payloads are reused from the repository.

## Full Deploy
Full Deploy remains available for troubleshooting or when a forced rebuild is desired.
