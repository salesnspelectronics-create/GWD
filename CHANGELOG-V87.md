# V87 — Rollback command result validation

- Check appcmd exit codes during rollback site-path restoration, site start, and failed-site removal.
- Propagate command failure to WizardForm so cleanup warnings are reported instead of silently continuing.
- No changes to IIS installations, databases, or deployed applications.

Validation: static source assertions and ZIP CRC only; .NET build and Windows runtime tests NOT RUN.
