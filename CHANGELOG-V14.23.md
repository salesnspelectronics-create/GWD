# V14.23 — Remote IIS Compile Fix

Focused regression fix based on V14.22.

## Fixed
- Rewrote embedded PowerShell used by `RemoteDeploymentService` using C# raw string literals.
- Removed invalid quote/interpolation escaping that caused cascaded C# compiler errors such as `Too many characters in character literal`, `Newline in constant`, `Unexpected character '$'`, and namespace/member parse failures.
- Preserved V14.22 Remote IIS deployment, remote readiness/bootstrap behavior, reusable Deployment Repository, offline package location, and Project Lifetime Analytics.

## Scope
No intentional functional changes outside the V14.22 Remote IIS string/compile regression and version metadata.
