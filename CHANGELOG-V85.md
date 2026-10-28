# V85 — Single Folder rollback safety

- Prepare rollback payload in sibling directory before changing hosted directory.
- Swap via directory rename, restoring displaced folder if activation rename fails.
- Preserve displaced folder on cleanup failure; do not interrupt swap after it starts.
- No IIS deployment or database changes performed.
- Windows build and runtime regression tests NOT RUN.
