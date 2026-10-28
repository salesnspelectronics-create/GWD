# V65 — IIS File-Lock-Aware Activation

- Fixes V64 runtime failure where `C:\inetpub\wwwroot` remained locked after IIS stop.
- Confirms target application pool reaches Stopped state before hosted-folder replacement.
- Replaces one-shot hosted-folder deletion with bounded lock-aware retry/backoff.
- Preserves the validated unique rollback backup if the lock cannot be released.
- Emits explicit deployment diagnostics for app-pool stop confirmation, lock retries, and folder release.
- V64 health indicator / shared SQL state behavior is preserved.
