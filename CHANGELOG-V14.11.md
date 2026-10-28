# Generic Web Deployment Manager V14.11

- Added **Use IIS default web root** mode:
  - Host directly in `C:\inetpub\wwwroot`.
  - Force HTTP port 80.
  - Stop/remove the IIS site currently owning port 80 and create the selected application site after confirmation.
  - Existing web-root content is staged/rolled back using the existing Single Folder safety workflow.
- Moved Host Header / Health Check under **Advanced IIS settings**.
- Review page no longer duplicates Current Operation in the global lower footer; the Review status line is the single activity display.
- Elapsed deployment timer is larger, bold, blue while running, green on success and red on failure.
- Port-80 URLs are shown as `http://host` without `:80`.
- Added an explicit database-behavior note: changing Database Name switches databases; Deployment Manager does not silently delete/migrate the previous database.
- Version updated to V14.11.
