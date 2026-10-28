# V62 Development Report

V61 reached the IIS activation path but failed because rollback used a fixed sibling path (`C:\inetpub\wwwroot.__rollback`) that already existed. V62 removes that collision-prone design.

Each deployment now gets a unique backup under the configured persistent deployment root. Backup preparation happens before IIS stop, so a backup failure cannot unnecessarily take the site offline. The activation and failure-cleanup paths reference the same active backup. Successful deployments retain rollback history with a five-backup retention policy.

Static/source checks were completed. Windows/.NET/IIS execution is unavailable in this environment, so runtime acceptance remains pending.
