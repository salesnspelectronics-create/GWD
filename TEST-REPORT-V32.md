# V32 Test Report

## Static checks performed
- Verified V32 version metadata and main UI labels.
- Verified wizard has 11 unique stages including Offline Dependencies.
- Verified Auto Deploy has an Offline Dependencies validation branch.
- Verified Deploy button cancellation routes through the same footer button.
- Verified only one `stagedPublish` local declaration remains.
- Verified only one hosted-folder LinkClicked handler remains.
- Verified complete npm preparation/validation uses clean temporary folders.
- Verified ZIP source integrity after packaging.

## Runtime limitation
A Windows/.NET 9 SDK + IIS runtime is not available in this environment, so `dotnet build`, npm online/offline network isolation, IIS deployment, and cancellation process-tree behavior were not runtime-certified here. These must be tested on the target Windows machine.
