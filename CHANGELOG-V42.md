# Generic Web Deployment Manager V42

## Source-first identity and stable workspace

- Project identity is populated after a valid ASP.NET source is selected.
- Versioned release wrappers are removed during workspace synchronization: the real repository contents (`src`, solution files, and related source metadata) are copied directly into the one stable application folder.
- `node_modules`, `bin`, and `obj` remain in that stable folder and are reused by later versions.
- Source files removed by a later release are removed from the stable workspace.

## Dependency workflow

- Each application session starts in **Offline Only** mode.
- Offline repository preparation and readiness testing are optional wizard actions; choosing Offline Only no longer disables **Next**.
- The actual build still performs the authoritative offline dependency check and stops with a clear missing-package error.

## Remote and export fixes

- Remote IIS targets require an IPv4 address.
- Authentication failures are reported as an incorrect remote username/password.
- Added **Save securely** for remote IP, username, and password. Password data is protected with Windows DPAPI for the current Windows user and stored outside the application directory.
- Diagnostic Bundle export now writes directly to Desktop.
- Removed the stale local-only validation that rejected a selected Remote IIS target.

## Version clarity

- Fast Local Test still compiles only and intentionally does not replace the hosted IIS application. To make the browser show a newly compiled version, use **Fast IIS Update** or **Full IIS Deploy**.
