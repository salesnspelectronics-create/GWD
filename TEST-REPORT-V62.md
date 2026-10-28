# V62 Test Report

## Evidence available in this environment

PASS — static source inspection confirms no deployment backup path is generated as `<hostingRoot>.__rollback`.

PASS — unique backup path is generated under `DeploymentRoot\Backups\Application\timestamp-guid`.

PASS — backup copy/validation occurs before `StopSiteForDeploymentAsync` in the Single Folder activation path.

PASS — activation failure and cleanup rollback both restore from the active unique backup.

PASS — successful deployment retains backups and prunes history to the newest five.

PASS — version metadata is 62.0.0.

## Not verified here

The current environment does not provide the Windows/.NET/IIS runtime required to compile and execute the application. Therefore build, IIS deployment, rollback, and runtime acceptance are PENDING and are not reported as PASS.

## Required Windows acceptance

1. Run Prepared IIS Publish against NSP Office with an existing stale `C:\inetpub\wwwroot.__rollback` folder.
2. Confirm deployment does not collide with that stale folder.
3. Confirm a unique backup is created under `C:\WebDeployments\Backups\NSP Office\...` (or the configured DeploymentRoot equivalent).
4. Confirm IIS is stopped only after backup preparation succeeds.
5. Confirm successful deploy + health check.
6. Force a post-activation failure in a controlled test and confirm exact backup restoration.
