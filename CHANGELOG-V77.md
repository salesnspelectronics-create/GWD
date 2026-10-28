# V77 — Source tree release identity gate

Before staging acceptance, fail closed if the frontend is outside the selected backend API project or source root. This complements Git root and hash checks.

No live deploy or database operations. Static validation only. Independent runtime UI/API version verification is still pending.
