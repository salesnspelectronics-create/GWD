# V68 — Smart Auto-Verify UI & Unified SQL/Database
- V68 UI baseline follows the approved Smart Auto-Verify mockup: larger status-driven sidebar, clickable completed stages, cleaner footer.
- Background verification starts after source selection and checks Environment, IIS, runtimes, SQL/database and deployment location without blocking the UI.
- Healthy stages are marked Auto verified and navigation can skip them; every stage remains directly accessible from the sidebar.
- SQL Server and Database are one wizard stage; database verification is integrated into the SQL page.
- IIS/SQL/HTTP health text removed from the footer; stage health is represented in the sidebar.
- Fixed IIS site-list DataGridView race: async enumeration now verifies the grid is alive and its columns are initialized before adding rows; same guard added for app-pool grid.
- Version 68.0.0.
