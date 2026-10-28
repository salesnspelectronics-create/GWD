# V14.15

- Deployment mode is now visible on **Review & Deploy** immediately before deployment.
- Added explicit **Smart Deploy (Recommended)** and **Full Deploy** choices.
- Smart Deploy remains safety-first: backend/database changes or uncertain state fall back to the full path.
- Full Deploy explicitly rebuilds all normal application stages.
- Renamed the old ambiguous `Automatic` / `Full rebuild` labels to `Smart Deploy` / `Full Deploy` in the target page.
