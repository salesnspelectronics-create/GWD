# V14.25 Test Report

## Static verification performed
- Confirmed V14.25 version markers in project and main UI.
- Confirmed `ApplyTargetModeState()` is invoked immediately after wiring `SelectedIndexChanged`, fixing the first-render Local Server enable-state bug.
- Confirmed Local mode disables Host/IP and all Remote/WinRM credential controls.
- Confirmed Remote mode enables those controls.
- Confirmed Repository, Source and Ready Publish path rows use a two-column fill + Browse layout.
- Confirmed wizard starts with `FormWindowState.Maximized`.
- Confirmed no second `SanitizeFolderName` definition was reintroduced.
- ZIP integrity checked after packaging.

## Not performed in this environment
- Actual .NET 9 Windows Forms compile (dotnet SDK unavailable).
- Runtime Windows UI layout test.
- Live WinRM / Remote IIS connection test.

Runtime verification is required on Windows.
