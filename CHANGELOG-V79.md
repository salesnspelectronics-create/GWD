# V79 — Live frontend release identity

Creates a fresh release ID and a public JSON marker in the staged frontend, hashes the complete staged wwwroot including the marker, and verifies the marker through HTTP after activation. A mismatch, missing marker or mismatched activated asset hash fails closed before success. This verifies which frontend release IIS serves, not the version label rendered by React. Prepared Publish is exempt.

No production deploy or database changes. Windows .NET build and synthetic HTTP tests were not run.
