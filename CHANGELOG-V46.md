# Generic Web Deployment Manager V46

## Diagnostic conclusion

The supplied V45 bundles showed `esbuild@0.25.12` failing with `spawnSync ... esbuild.exe EFTYPE`. They also proved that Full IIS Deploy was running npm in the versioned Desktop source instead of the selected stable workspace.

## Fixes

- Stable workspace synchronization now applies to every source-based mode:
  - Fast Local Test
  - Fast IIS Update
  - Full IIS Deploy
  - Visual Studio prepared
- Ready publish folder mode remains copy-only and does not require source synchronization.
- Incoming source is logged as read-only; build tools run from the stable application workspace.
- Added explicit detection for broken/incompatible esbuild/rollup native executables (`EFTYPE`, `ENOEXEC`, invalid Win32 application).
- On this specific failure, only `node_modules/esbuild` and `node_modules/@esbuild` inside the stable workspace are removed, then the selected dependency-mode npm command is retried once.
- If retry still fails, the report clearly states that native-binary repair was attempted; it is no longer mislabeled as a generic Hybrid registry failure.
- Preserves the V45 Node 8192 MB heap guard and safe IIS object checks.
