# V37 — Fast Change: Remote Validation + Auto-Continue Confirmation

- Remote IIS target validates IPv4, user, password, and WinRM port before Next/Test.
- Host/IP input accepts IPv4-style characters only and displays an example when invalid.
- Remote Test stops immediately with a clear validation message when credentials/IP are missing.
- Target-page navigation now supports valid Remote IIS targets instead of treating every non-local host as invalid.
- Default IIS/wwwroot final takeover confirmation uses a visible 5-second Yes countdown; No cancels, otherwise Yes runs automatically.
- V35 Fast Change behavior retained: Start Over -> Target & Source, Offline Only default, visible dependency sync state/result, Auto Deploy skips repeated full repository scans.
- V36 Build Performance trace retained.
