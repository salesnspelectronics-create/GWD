# Deployment Manager V14.7

- Fixes V14.7 compile failure by defining and wiring the deployment elapsed-time state/label.
- Adds live deployment elapsed time on Review & Deploy and logs final duration on success or failure.
- Adds UI log levels: Minimal, Normal, Verbose. Disk log remains complete in every mode.
- Reclassifies benign npm stderr diagnostics (timing, verbose/info, HTTP GET 200, optional cross-platform dependencies) so they are not shown as false ERR lines.
- Keeps Review & Deploy split roughly 50/50 between plan summary and live log.
- Open deployed site / Open hosted folder remain disabled until a successful deployment is available.
