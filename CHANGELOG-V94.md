# V94 - N14 Cleanup failure propagation

- Report and propagate failed deployment folder deletion as an IOException with the original IO error preserved as InnerException.
- No changes to deployment activation or IIS operations.
- Build and Windows runtime tests NOT RUN.
