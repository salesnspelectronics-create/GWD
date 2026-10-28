# Test Report — V14.26

## Static checks
- Fast Build is default in DeploymentProfile, Start Over, Source page and Review page.
- No remaining active Smart Deploy selection path in WizardForm.
- Persistent online npm cache resolves under Deployment Repository/BuildCache/npm-cache.
- Persistent online NuGet cache resolves under Deployment Repository/BuildCache/nuget-packages.
- Offline cache remains under Deployment Repository/OfflinePackages.
- Version metadata updated to 14.26.

## Runtime
A Windows/.NET 9 runtime deployment benchmark is still required on the user's deployment machine.
