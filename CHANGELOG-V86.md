# V86 — Activation failure and cancellation recovery

- Guard deletion of the hosted target within activation try/catch.
- Use a non-cancelled token for compensating rollback after activation failure.
- Surface both original and rollback exceptions when recovery fails.
- No production deployment performed.
