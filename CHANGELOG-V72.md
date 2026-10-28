# V72 — Workspace sync fix

Version 72.0.0; target net9.0-windows; baseline is the user-provided complete V71 Unified SQL archive.

Root cause: PrepareStableWorkspace trusted the previous manifest source hash plus destination existence/length. It never hashed the actual destination before classifying Unchanged. Same-size workspace corruption or stale contents could therefore survive.

Fix: compare actual source/workspace SHA256 for every non-excluded file; copy changed content; remove stale non-excluded files including untracked compile inputs. Re-read and verify both complete file sets against the pre-sync source snapshot. Log Added/Updated/Removed/Unchanged/Verified/Mismatch; mismatches include relative path and both hashes. Any mismatch or IO/cancellation failure aborts before staging/build/publish. Promote the manifest only after successful verification. Changed files receive a fresh write time for incremental compiler invalidation. An exclusive workspace lease spans verification and publish.

Explicit exclusions: .git, IIS-Publish, bin, obj, node_modules; existing .vs, dist, out exclusions retained. Only root manager manifest/temp/lock metadata is additionally reserved. No extension-based .cs exclusion. Non-excluded links fail closed.

Pipeline contract: future Git update completes in Incoming -> source/API validation -> workspace sync -> mandatory SHA256 verification -> staging/build/publish. No Git integration added. Prepared IIS Publish retains its existing source-free flow.

V71 SQL strategy, diagnostics/rollback, profiles, source lock, clean export and UI are preserved. See evidence/V72-provenance.json and byte-preservation tests.
