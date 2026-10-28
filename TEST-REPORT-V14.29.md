# V14.29 Test Report

## Static checks performed in assistant environment
- V14.28 full source used as baseline.
- Version metadata updated to 14.29.
- Verified exactly one `SanitizeFolderName(string)` definition remains.
- Verified Offline Only deployment no longer calls automatic online package preparation.
- Verified Offline npm repair uses `npm install --offline` with the shared repository cache.
- Verified offline NuGet configuration clears online sources.
- Verified Local First configuration adds nuget.org only in the non-offline config.
- Verified persistent repository paths are outside source/version folders.
- Verified Fast Build remains the profile default.

## Runtime limitations
The assistant environment does not contain the Windows .NET SDK/MSBuild/IIS stack, so an actual Windows build, NuGet restore, npm restore, IIS deployment, and network-disconnect test could not be executed here. These are not claimed as passed.

## Recommended operator verification
1. Select repository on D:, e.g. `D:\Developer-Packages`.
2. With Internet connected, choose `Sync Missing Online Once`.
3. Confirm `OFFLINE BUILD READY`.
4. Disconnect Internet physically or disable the adapter.
5. Keep Dependency Mode = `Offline Only` and Fast Build = default.
6. Build/deploy the same source and confirm no NuGet/npm HTTP access occurs.
7. Add a new package/version not present locally and confirm Offline Only fails with a missing-dependency message rather than going online.
8. Reconnect, run `Sync Missing Online Once`, disconnect again, and retry.
