# V93 — Activation rollback safety

- Restore the prior hosted folder from a prepared copy rather than deleting the active folder before copying.
- Preserve the displaced folder if cleanup fails.
- Keep original activation exception and rollback exception via AggregateException.
- No live deployment or database changes.
- .NET Build and Windows runtime tests: NOT RUN.
