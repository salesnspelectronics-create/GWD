# V34 — Auto Offline Preparation / Cancel Auto Deploy

- Auto Deploy can now be cancelled from the same Auto Deploy button while automation is running.
- After cancellation or an attention stop, normal Back/Next navigation is restored.
- Offline Dependencies is now self-healing during Auto Deploy: if Offline Only validation fails, Auto Deploy automatically runs the complete online-once repository preparation, validates offline readiness, then continues when ready.
- Offline preparation UI reports persistent repository total size, bytes added during the current preparation, and the most recently changed repository file while work is running.
- Manual Prepare Complete Offline Repository remains available.
- Existing Safe Fast Deploy, persistent repository, Deploy/Cancel Deploy, history and remote deployment features are preserved.
