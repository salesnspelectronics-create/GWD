# Generic Web Deployment Manager V44

- Build/deployment mode is selected on the first Project Source page.
- Review & Deploy no longer contains a mode selector and cannot silently change the established plan.
- Removed the duplicate Dependencies wizard page. Dependency mode, online sync, and offline-test actions remain together on Deployment Settings.
- Start Over returns to Project Source and preserves the previously selected source for confirmation or replacement.
- Reduced the segmented IPv4 control to a normal single-row height.
- Hosted site/folder links are available whenever an existing hosted folder is present, including after a compile-only Fast Local Test.
- Added explicit mode explanations: Fast Local Test never changes IIS; Fast IIS Update is the quick option that updates the browser-visible application.
- Review explicitly reports whether the hosted website will be updated or left unchanged.
- Preserved stable workspace caching, Offline/Hybrid/Online modes, diagnostics, crash capture, history, cancellation, rollback and health checks.
