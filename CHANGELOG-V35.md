# V35 — Fast Change

## Fast workflow changes
- Start Over now returns directly to **Target & Source** (Target / Source / Build Mode) instead of Welcome.
- Dependency Mode is forced to **Offline Only** on every application start, Start Over, and source change. Online / Local First are session-only choices.
- **Sync Missing Online Once** now has an explicit busy state: Next, Back, Auto Deploy, dependency mode, and dependency actions are locked while synchronization runs; a marquee progress indicator and status text are shown; a clear final READY/INCOMPLETE result is displayed.
- Auto Deploy no longer performs an offline repository readiness scan or automatic repository preparation on every run. It assumes the persistent repository is ready and proceeds immediately.
- If the real Offline Only restore/build detects a missing cached dependency, Auto Deploy stops and returns to **Offline Dependencies** with instructions to sync/prepare the repository.
- Removed the automatic offline-readiness scan that previously ran whenever the Offline Dependencies page was opened.
- Preserved Fast Build default, persistent dependency repository, Safe Fast Deploy fingerprints, Auto Deploy cancellation, Deploy -> Cancel Deploy, and hand cursor behavior.
