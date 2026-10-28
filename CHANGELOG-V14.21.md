# Generic Web Deployment Manager V14.21

## Project Lifetime / Deployment Analytics

Implemented the complete `NG-001 — Project Lifetime / Deployment Analytics` backlog item as a first-class application feature.

### Persistent history

- Every deployment attempt now writes a persistent start event before the deployment pipeline begins.
- Successful, Failed, and Cancelled outcomes are finalized with the same attempt ID.
- History survives Deployment Manager restarts and is independent of the EXE location.
- History is stored under the generic Windows application-data location:
  - `C:\ProgramData\GenericWebDeploymentManager\History\`
- Storage is append-oriented JSON Lines (`.jsonl`) with a flush-to-disk write after each event.
- Existing Smart Deploy state remains completely separate from analytics history.
- No passwords, tokens, SQL passwords, connection-string secrets, or credentials are stored.

### Project identity

Repeated deployments are grouped by a stable identity derived from:

- Application Name
- IIS Site Name
- Database Name

Source-folder path is recorded for each attempt but is intentionally not part of the stable project key, so moving a release/source folder does not fragment the same project's lifetime history.

### Per-attempt data

The history records:

- Application / project identity
- Source path
- Deployment target path
- IIS site
- Database name
- Detected application/release version when available
- Start time
- End time
- Elapsed deployment time
- Successful / Failed / Cancelled outcome
- Smart / Full / Prepared deployment mode
- Offline-build flag
- Session log path
- Elapsed interval since the previous completed deployment attempt

### Lifetime metrics

The application calculates and displays:

- Total deployment attempts
- Successful deployments
- Failed deployments
- Cancelled deployments
- Incomplete/interrupted attempts
- Distinct successfully deployed versions when version metadata can be detected
- First deployment date
- Latest deployment date
- Calendar lifetime
- Cumulative deployment runtime
- Average deployment duration
- Average successful deployment duration
- Fastest deployment
- Slowest deployment
- Average interval between deployments
- Smart Deploy count
- Full Deploy count
- Prepared deployment count

### User interface

- Added a persistent **Deployment History** button to the main wizard footer.
- Added a dedicated **Deployment History / Project Lifetime** window.
- Project selector allows switching between previously deployed applications.
- **Lifetime Summary & Timeline** tab shows project-wide metrics plus the chronological deployment/release timeline.
- **Deployment History** tab shows individual attempts including duration, interval, mode, result, source, target, and log location.
- A selected deployment's original session log can be opened directly.
- The persistent History folder can be opened directly from the History window.

### Version detection

Application/release version is detected generically from:

1. A version-like token in the selected source-folder name (for example `V1.11.25`).
2. As fallback, `Version`, `FileVersion`, or `AssemblyVersion` in the detected ASP.NET project file.

No NSP-specific application logic or package names are hard-coded into the analytics engine.

### Regression / safety

- Online Build behavior is unchanged.
- Offline Build and Offline Auto-Prepare behavior from V14.20 are unchanged.
- Smart Deploy state and analytics history are independent.
- Prepared Publish remains available.
- Backup, SQL/database preservation, IIS/AppPool, Health Check, Rollback, Logging, Error Report, Start Over, and Cancel paths remain in the existing deployment pipeline.
- Start Over does not erase lifetime history.
