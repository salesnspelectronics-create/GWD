# V14.24 — Duplicate Sanitize Compile Fix

Focused compile-fix release based directly on V14.23.

## Fixed
- Removed the duplicate `SanitizeFolderName(string)` method from `DeploymentService.cs` that caused CS0111 / "already defines a member called SanitizeFolderName with the same parameter types".
- Retained one shared sanitizer implementation for both offline-repository folder naming and release-folder naming.
- Preserved V14.23 Remote IIS, Shared Deployment Repository, Offline Build, deployment history, rollback, IIS, SQL, health-check and logging behavior.

## Scope
No feature redesign in this release; compile regression only.
