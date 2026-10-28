# V100 — Recovery preparation deadline

- Add a ten-minute cooperative deadline to preparation of rollback copies in both recovery paths.
- Never cancel the actual folder swap.
- An individual synchronous File.Copy cannot be interrupted: this is not a strict wall-clock limit.
- Build/runtime tests NOT RUN.
