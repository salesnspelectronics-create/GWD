# TEST REPORT — V14.22

## Performed
- Source-level structural checks.
- Verified V14.22 metadata.
- Verified remote credential is not written by AppDataService/history.
- Verified offline repository path is derived from DeploymentRepositoryFolder rather than SourceFolder.
- ZIP integrity check.

## Not performed
- No actual Windows WinRM remote connection.
- No actual remote IIS enable/install.
- No actual remote Hosting Bundle installation.
- No real remote deployment/rollback runtime test.
- No dotnet build because the authoring container lacks the Windows .NET toolchain.
