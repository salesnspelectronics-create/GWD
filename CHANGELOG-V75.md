# V75 — Release artifact integrity gate

Before source-based deploy, require fresh frontend output, compare SHA256 of built frontend and staged wwwroot, and write gwd-release-integrity.json with source root, component paths, source version, and frontend hash. Prevent hosted frontend reuse.

**Remaining HIGH:** independent frontend/backend release version and commit equality verification, including post-deploy checks.

Static checks only; no dotnet build, synthetic runtime test or live deployment.
