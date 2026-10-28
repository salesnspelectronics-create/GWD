# V14.17

- Start Over now offers two explicit modes: **Clear Log** and **Keep Log**.
- **Start Over — Clear Log** clears the visible log, rotates to a new independent session log, and keeps the previous disk log for audit/debugging.
- **Start Over — Keep Log** resets the wizard while preserving the visible log and continuing the same log session.
- Replaced **Copy Error Report** with **View Error Report**.
- Added an Error Report viewer with **Dump Error Report**.
- Error dump is a ZIP diagnostic bundle containing the error report, current full session log, deployment state when available, and a manifest.
- Diagnostic report header corrected to **V14.17**.
- Session log filenames now include a short unique suffix to prevent collisions when Start Over creates a new session quickly.
- Existing Smart Deploy / Full Deploy / cancellation / rollback behavior remains unchanged.
