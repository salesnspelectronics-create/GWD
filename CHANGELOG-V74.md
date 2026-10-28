# V74 — Source Git provenance gate
- Before workspace sync, resolve canonical incoming Git root.
- Verify API and ClientApp are in that same Git repository/worktree.
- Reject nested worktrees and divergent component Git roots before Build.
- Log verified HEAD; for clean source exports without Git metadata retain workspace SHA256 verification.
- No production deployment or database operations performed.
- Validation: static assertions only; Windows/.NET build and synthetic runtime tests not executed.
- Pending: release-version comparison and post-deploy UI/API manifest verification.
