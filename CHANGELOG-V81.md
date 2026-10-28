# V81 — HIGH mixed-version regression gate

- Centralized release-version comparison in `ReleaseVersionContract.Matches` and wired it into staging UI verification, live UI HTTP verification, and live API verification.
- Added dependency-free .NET 9 console regression suite with 11 cases including backend 573 + frontend 560, missing/unknown versions, matching versions and malformed length.
- Run on Windows with .NET 9 SDK: `dotnet run --project tests/ReleaseVersionRegression/ReleaseVersionRegression.csproj`.
- This is a source-level regression suite for the shared version comparison logic, **not** a full IIS/HTTP end-to-end simulation. No production deployment or database operation performed.
