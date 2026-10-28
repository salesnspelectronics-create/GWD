# V89 — N8

Recovery actions in CleanupFailedDeploymentAsync use an independent non-cancelled token, preventing cancellation of the original deployment from aborting compensation. No IIS execution performed.
