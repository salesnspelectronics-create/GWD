# V78 — Running API release version gate

After activated-file verification and before declaring deployment success, query `/api/v1/health`, require a version field, and compare it to the source version in the release integrity manifest. Mismatch, absent version, HTTP failure, or malformed response fails closed and enters the existing deployment error/rollback path. Prepared IIS Publish is exempt because its source-version identity is not established.

**Known gap:** UI version is not independently observed from the browser/runtime; a UI version contract and end-to-end Windows/IIS tests are still required. No live deploy or database operation was performed.
