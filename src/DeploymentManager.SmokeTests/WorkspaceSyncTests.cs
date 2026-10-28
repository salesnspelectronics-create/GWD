using GenericWeb.DeploymentManager.Services;
using GenericWeb.DeploymentManager.Models;
using System.Reflection;

namespace GenericWeb.DeploymentManager.SmokeTests;

internal static class WorkspaceSyncTests
{
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("PASS: " + label);
    }
    public static void Run(string root)
    {
        var incoming = Path.Combine(root, "incoming");
        var workspaceRoot = Path.Combine(root, "workspaces");
        var workspace = Path.Combine(workspaceRoot, "NSPOffice");
        const string relative = "src/NSPOffice.Api/Program.cs";
        void Write(string folder, string path, string text)
        {
            var full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        // Synthetic health-route regression, padded to equal byte count to expose V71's bug.
        const string routes = "app.MapGet(\"/api/v1/health\", () => \"OK\");\napp.MapGet(\"/api/v1/health/live\", () => \"OK\");\n";
        var old = routes + routes;
        var fresh = routes.PadRight(old.Length, ' ');
        Write(incoming, relative, fresh);
        Write(incoming, "src/NSPOffice.Api/NSPOffice.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        Write(incoming, "Directory.Build.props", "<Project />");
        Write(incoming, "global.json", "{}");
        Write(incoming, "package-lock.json", "{}");
        var first = WorkspaceSynchronizer.Sync(incoming, workspace, Console.WriteLine);
        Check(first.Added == 5 && first.Verified == 5, "Initial sync verifies all source and build-critical files");
        Write(workspace, relative, old);
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(incoming, relative), stamp);
        File.SetLastWriteTimeUtc(Path.Combine(workspace, relative), stamp);
        Check(new FileInfo(Path.Combine(incoming, relative)).Length == new FileInfo(Path.Combine(workspace, relative)).Length,
            "Regression fixture has identical sizes and timestamps, manifest already trusts Incoming");
        Write(workspace, "src/NSPOffice.Api/Removed.cs", "// untracked stale compile input");
        foreach (var excluded in new[] { ".git", "IIS-Publish", "bin", "obj", "node_modules", ".vs", "dist", "out" })
        {
            Write(incoming, excluded + "/excluded.cs", "source-cache");
            Write(workspace, excluded + "/excluded.cs", "workspace-cache");
        }
        var runner = new CommandRunner(); runner.Output += Console.WriteLine;
        var deployment = new DeploymentService(runner, new SqlServerService(runner, new PrerequisiteService(runner)), new AppDataService());
        var profile = new DeploymentProfile { SourceFolder = incoming, WorkspaceRoot = workspaceRoot, ApplicationName = "NSPOffice",
            ApiProjectPath = Path.Combine(incoming, "src/NSPOffice.Api/NSPOffice.Api.csproj") };
        var result = typeof(DeploymentService).GetMethod("PrepareStableWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(deployment, new object[] { profile, CancellationToken.None })!;
        var builtProfile = (DeploymentProfile)result.GetType().GetProperty("Profile")!.GetValue(result)!;
        Check(builtProfile.SourceFolder == workspace && builtProfile.ApiProjectPath.StartsWith(workspace), "Production pipeline remaps build to verified workspace");
        Check(File.ReadAllText(Path.Combine(workspace, relative)) == fresh && WorkspaceSynchronizer.Hash(Path.Combine(incoming, relative)) == WorkspaceSynchronizer.Hash(Path.Combine(workspace, relative)),
            "Production sync overwrites stale Program.cs despite equal metadata and trusted manifest; SHA256 equal");
        Check(File.GetLastWriteTimeUtc(Path.Combine(workspace, relative)) > stamp,
            "Changed build input gets fresh timestamp so incremental compiler cannot trust old source timestamp");
        Check(!File.Exists(Path.Combine(workspace, "src/NSPOffice.Api/Removed.cs")), "Untracked stale .cs removed without relying on manifest");
        Check(new[] { ".git", "IIS-Publish", "bin", "obj", "node_modules", ".vs", "dist", "out" }.All(d => File.ReadAllText(Path.Combine(workspace, d, "excluded.cs")) == "workspace-cache"), "Explicit cache exclusions preserved");
        var repeat = WorkspaceSynchronizer.Sync(incoming, workspace, Console.WriteLine);
        Check(repeat.Unchanged == 5 && repeat.Verified == 5 && repeat.Updated == 0, "Unchanged still receives full mandatory hash verification");
        var beforeFailure = File.ReadAllText(Path.Combine(workspace, ".gwd-workspace-manifest.json"));
        bool mutated = false, blocked = false; var logs = new List<string>();
        try
        {
            WorkspaceSynchronizer.Sync(incoming, workspace, line =>
            {
                logs.Add(line); Console.WriteLine(line);
                if (!mutated && line.Contains("Unchanged:")) { mutated = true; Write(workspace, relative, old); }
            });
        }
        catch (InvalidOperationException ex) { blocked = ex.Message.Contains("Build/Publish blocked"); }
        Check(blocked && logs.Any(l => l.Contains("Mismatch: " + relative) && l.Contains("source SHA256=") && l.Contains("workspace SHA256=")), "Post-sync corruption blocks Build/Publish with path and both hashes");
        Check(File.ReadAllText(Path.Combine(workspace, ".gwd-workspace-manifest.json")) == beforeFailure, "Failed verification cannot promote manifest");
        WorkspaceSynchronizer.Sync(incoming, workspace, Console.WriteLine);
        mutated = false; blocked = false;
        try
        {
            WorkspaceSynchronizer.Sync(incoming, workspace, line =>
            {
                if (!mutated && line.Contains("Unchanged:")) { mutated = true; Write(incoming, relative, old); Write(workspace, relative, old); }
            });
        }
        catch (InvalidOperationException) { blocked = true; }
        Check(blocked, "Incoming mutation during sync rejected even when both current hashes match");
        Write(incoming, relative, fresh);
        File.WriteAllText(Path.Combine(workspace, ".gwd-workspace-manifest.json"), "broken manifest");
        Check(WorkspaceSynchronizer.Sync(incoming, workspace, Console.WriteLine).Verified == 5, "Corrupt legacy manifest cannot suppress repair or verification");
        var publishRunner = new CommandRunner();
        var commands = 0; mutated = false; blocked = false;
        publishRunner.Output += line =>
        {
            Console.WriteLine(line);
            if (line.StartsWith(">>>")) commands++;
            if (!mutated && line.Contains("Unchanged:")) { mutated = true; Write(workspace, relative, old); }
        };
        var publisher = new DeploymentService(publishRunner, new SqlServerService(publishRunner, new PrerequisiteService(publishRunner)), new AppDataService());
        profile.BuildMode = "Fast Local Test";
        profile.DeploymentRoot = Path.Combine(root, "deployment");
        try { publisher.PublishAsync(profile).GetAwaiter().GetResult(); }
        catch (InvalidOperationException ex) { blocked = ex.Message.Contains("Build/Publish blocked"); }
        Check(blocked && commands == 0 && !Directory.Exists(Path.Combine(profile.DeploymentRoot, "Staging")),
            "Actual PublishAsync mismatch stops before staging or any external build/publish command");
        WorkspaceSynchronizer.Sync(incoming, workspace, Console.WriteLine);
        var acquire = typeof(DeploymentService).GetMethod("AcquireWorkspaceLease", BindingFlags.Static | BindingFlags.NonPublic)!;
        using (var lease = (FileStream)acquire.Invoke(null, new object[] { profile })!)
        {
            blocked = false;
            try { publisher.PublishAsync(profile).GetAwaiter().GetResult(); }
            catch (IOException) { blocked = true; }
            Check(blocked && commands == 0, "Concurrent pipeline cannot modify/build a leased workspace");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); blocked = false;
        try { WorkspaceSynchronizer.Sync(incoming, workspace, Console.WriteLine, cancelled.Token); }
        catch (OperationCanceledException) { blocked = true; }
        Check(blocked, "Cancellation stops pipeline");
        Console.WriteLine("WORKSPACE REGRESSION TESTS COMPLETE");
    }
}
