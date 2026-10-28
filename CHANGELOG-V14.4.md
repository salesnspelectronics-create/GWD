# Deployment Manager V14.4.1

- Added Upgrade Existing Deployment behavior when the selected port is already owned by the same IIS Site Name.
- IIS Site page distinguishes free ports, same-site reusable ports, ports owned by another IIS site, and ports owned by another process/service.
- Next is allowed for a same-site reusable binding.
- Deployment stops/replaces only the matching IIS site; it does not stop IIS globally or disturb unrelated sites.
- Existing health check and rollback behavior remain in place.
