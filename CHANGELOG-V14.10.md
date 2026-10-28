# V14.10

- Hosting folder auto-name now follows the Application Name instead of legacy `WebApplication`/`NSPOfficeAutomation` defaults.
- Deployment Location shows the selected application name explicitly.
- Changing Application Name updates only auto-generated hosting paths; manually browsed custom paths are preserved.
- A port owned by another IIS site is now considered deployable. Deployment Manager stops and removes the conflicting IIS site during deployment, then creates/starts the requested site on that port.
- Non-IIS process/service port conflicts remain blocked.
