# TEST REPORT V14.27

- Source baseline: V14.26.
- Verified version metadata changed to 14.27.
- Verified default `build-release.cmd` separates restore from publish and uses `--no-restore` during publish.
- Verified persistent NuGet cache path is outside the extracted source/version folder.
- Verified full fallback script exists.
- ZIP integrity checked after packaging.
- A real Windows/.NET 9 build was not executed in this environment.
