# Generic Web Deployment Manager — V58

## Scope
V58 consolidates dependency workflow on the first Source page, simplifies the Target page, refines Auto Deploy controls, and fixes diagnostic export UX.

## Changes
- Moved Shared Dependency Repository, Stable Build Workspace, Dependency Mode and dependency preparation actions to the first Source page.
- Kept `Sync Missing Online Once` and `Test Offline Build` on the Source page.
- Removed dependency/repository/workspace controls and dependency summaries from the Target page.
- Removed the unused legacy offline-dependencies page implementation.
- Footer navigation reordered so Auto Deploy is positioned after Start Over rather than between Back/Next.
- While Auto Deploy is active, Back and Next remain disabled and the button becomes `Cancel Deploy` (with step progress).
- Removed the duplicate `Diagnostics / Errors` button; the unified Diagnostics Center is the single diagnostics entry point.
- Reworked Diagnostics exports:
  - `Current Tab → Desktop` creates an actual ZIP file directly on Desktop.
  - `Current Tab → Other...` uses SaveFileDialog.
  - `All Diagnostics → Desktop` creates the full ZIP directly on Desktop.
  - `All Diagnostics → Other...` uses SaveFileDialog.
  - No FolderBrowserDialog is used for diagnostic exports.
  - Export completion shows the exact saved file path with `Open File` and `Open Folder`.
- Removed `Open Diagnostics Folder` from the main Diagnostics toolbar.
- Software/Project Evolution alignment advanced to V58.

## Compatibility / safety
- Existing settings paths and persistent repository/workspace behavior are preserved.
- Export content remains redacted for passwords, tokens, secrets, API keys, user IDs and connection strings.
- No destructive database behavior was added.
