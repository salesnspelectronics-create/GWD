# Generic Web Deployment Manager V14.12

- Added operator **Cancel deployment** during an active deployment.
- Cancellation propagates to npm/dotnet/appcmd/SQL operations; CommandRunner kills the active child process tree.
- After cancellation the manager removes staging/failed deployment files and restores the previous Single Folder deployment/IIS path when possible. Databases are intentionally not deleted by deployment cancellation.
- Added **Advanced IIS Management** directly on the IIS detection page.
- IIS site grid shows site name, state, bindings/ports, physical path and app pool.
- Multi-select operations: Start, Stop, Refresh, and **Remove selected + cleanup files**.
- Remove + cleanup deletes IIS site configuration, removes an unused dedicated app pool, and deletes the physical site folder/files.
- Added a second confirmation before deleting `C:\inetpub\wwwroot`; protected Windows/Program Files/drive-root paths are refused.
- IIS cleanup does not delete SQL databases.
