# V83 — N2 version metadata alignment

- Updated Directory.Build.props from 82.0.0 to 83.0.0.
- Aligned deployment-manager.version.json to 83.0.0.
- Replaced obsolete V72 static version assertion with dynamic equality check.
- No deployment logic or IIS changes.
- .NET restore/build and live IIS tests NOT RUN in this environment.
