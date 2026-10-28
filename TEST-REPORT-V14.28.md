# TEST REPORT V14.28

Static/source verification performed in the assistant environment:
- Version metadata updated to 14.28.
- Persistent BuildState path is outside source/version folders.
- Frontend fingerprint no longer relies only on one first package.json root.
- Fresh frontend output is explicitly overlaid into staging after backend publish.
- Local post-deploy verification compares current source fingerprints and activated wwwroot output hash.
- Persistent successful state commits only in FinalizeSuccessfulDeployment after health/verification.
- Fast Build remains default.

Runtime Windows/.NET 9 build, IIS deployment, npm/Vite build and remote WinRM deployment were not executable in this Linux environment and are not claimed as tested.
