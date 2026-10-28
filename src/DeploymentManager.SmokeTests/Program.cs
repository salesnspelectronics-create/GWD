using GenericWeb.DeploymentManager;
using System.Reflection;
using GenericWeb.DeploymentManager.Services;

namespace GenericWeb.DeploymentManager.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "GWDM-Smoke-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("GWDM_APPDATA_ROOT", testRoot);

        try
        {
            if (args.Contains("--workspace-sync"))
            {
                WorkspaceSyncTests.Run(testRoot);
                return 0;
            }
            if (args.Contains("--sql-strategy"))
            {
                SqlStrategyTests.RunAsync(testRoot).GetAwaiter().GetResult();
                return 0;
            }
            var appData = new AppDataService();
            var browseParent = Path.Combine(testRoot, "sources", "releases");
            var deletedVersion = Path.Combine(browseParent, "V53");
            Directory.CreateDirectory(deletedVersion);
            appData.SaveLastBrowseFolder(deletedVersion, BrowseContext.Source);
            Directory.Delete(deletedVersion, true);
            var fallback = appData.GetLastBrowseFolder(BrowseContext.Source);
            if (!string.Equals(Path.GetFullPath(fallback), Path.GetFullPath(browseParent), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Browse fallback failed. Expected '{browseParent}', got '{fallback}'.");

            var releaseRoot = Path.Combine(testRoot, "release-output");
            appData.SaveManagerReleaseRoot(releaseRoot);
            if (!string.Equals(appData.GetManagerReleaseRoot(), Path.GetFullPath(releaseRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Manager release/output settings persistence failed.");

            using var form = new WizardForm();
            var type = typeof(WizardForm);
            var showPage = type.GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.FullName, "ShowPage");
            var disposeContent = type.GetMethod("DisposeContentControls", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(type.FullName, "DisposeContentControls");
            var log = (TextBox?)(type.GetField("_log", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form))
                ?? throw new MissingFieldException(type.FullName, "_log");

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                showPage.Invoke(form, [9]);
                if (log.IsDisposed) throw new InvalidOperationException($"Log TextBox was disposed while entering Review on cycle {cycle}.");

                disposeContent.Invoke(form, null);
                if (log.IsDisposed) throw new InvalidOperationException($"Log TextBox was disposed during Start Over cleanup on cycle {cycle}.");

                log.Clear();
                showPage.Invoke(form, [0]);
                if (log.IsDisposed) throw new InvalidOperationException($"Log TextBox was disposed after returning to Project Source on cycle {cycle}.");
            }

            Console.WriteLine("PASS: smart browse parent fallback, settings persistence, and Review -> Start Over -> Project Source UI cycle checks passed.");
            var snapshotType = type.GetNestedType("DeploymentProfileSnapshot", BindingFlags.NonPublic)!;
            var profile = new Models.DeploymentProfile { ApplicationName = "Profile regression", SourceFolder = "test-source", SqlPassword = "SyntheticProfileSecret71" };
            var snapshot = snapshotType.GetMethod("From")!.Invoke(null, [profile, true])!;
            var json = System.Text.Json.JsonSerializer.Serialize(snapshot, snapshotType);
            var restored = new Models.DeploymentProfile();
            snapshotType.GetMethod("ApplyTo")!.Invoke(snapshot, [restored]);
            if (json.Contains(profile.SqlPassword) || restored.SourceFolder != profile.SourceFolder || !(bool)snapshotType.GetProperty("SourceLocked")!.GetValue(snapshot)!)
                throw new InvalidOperationException("Profile/Lock Source regression");
            Console.WriteLine("PASS: profile round-trip preserves source/lock state and excludes credentials.");
            var exportSource = Path.Combine(testRoot, "export-source"); Directory.CreateDirectory(exportSource);
            File.WriteAllText(Path.Combine(exportSource, "source.cs"), "// source");
            foreach (var dir in new[] { ".git", "IIS-Publish", "bin", "obj", "node_modules" })
            {
                Directory.CreateDirectory(Path.Combine(exportSource, dir));
                File.WriteAllText(Path.Combine(exportSource, dir, "excluded.txt"), "excluded");
            }
            var zipPath = Path.Combine(testRoot, "clean-export.zip");
            type.GetMethod("ExportCleanSourceZip", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                [exportSource, zipPath, new[] { ".git", "IIS-Publish", "bin", "obj", "node_modules" }]);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
                if (zip.Entries.Count != 1 || zip.Entries[0].FullName != "source.cs") throw new InvalidOperationException("Clean export exclusions regression");
            Console.WriteLine("PASS: clean source export retains source and excludes all five default generated folders.");
            for (var cycle = 0; cycle < 3; cycle++)
            {
                showPage.Invoke(form, [3]);
                var grids = Descendants(form).OfType<DataGridView>().ToArray();
                if (grids.Length != 2 || grids.Any(g => g.Columns.Count == 0 || g.IsDisposed))
                    throw new InvalidOperationException("IIS DataGridView construction regression");
                showPage.Invoke(form, [0]);
            }
            Console.WriteLine("PASS: IIS site/application-pool grids survive three page rebuild cycles (live async IIS refresh not exercised).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + (ex.InnerException ?? ex));
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch (Exception ex) { Console.Error.WriteLine("Smoke cleanup warning: " + ex.Message); }
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }
}
