# V99 — Live recovery verification

- Require HTTP health success after Single Folder rollback.
- Verify running API and frontend identity when backup has an integrity manifest.
- Legacy backups without a manifest require HTTP health and explicitly report identity NOT VERIFIED.
- Verification failures propagate as recovery failures.
- Build and Windows runtime tests NOT RUN.
