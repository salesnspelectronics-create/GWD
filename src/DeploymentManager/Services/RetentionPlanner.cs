namespace GenericWeb.DeploymentManager.Services;

// Reporting only. No delete API exists until durable resolution/ownership metadata
// can prove that an artifact is neither active nor a recovery point.
public static class RetentionPlanner
{
    public sealed record Artifact(string Path, long? Bytes, string Status, string Reason);
    public sealed record Capacity(string Root, long? FreeBytes, long? ObservedBytes, string? Warning);
    public sealed record Plan(bool DryRun, bool AutomaticDeletion, int MinimumKeep, int MinimumAgeDays,
        Artifact[] Artifacts, Capacity[] Capacity, string[] EligibleForDeletion);

    public static Plan Inspect(IEnumerable<string> roots, int minimumKeep = 5, int minimumAgeDays = 30)
    {
        if (minimumKeep < 1 || minimumAgeDays < 1) throw new ArgumentOutOfRangeException(nameof(minimumKeep));
        var artifacts = new List<Artifact>();
        var capacity = new List<Capacity>();
        foreach (var supplied in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            long? total = null;
            long? free = null;
            string? warning = null;
            try
            {
                var root = DeploymentPathSafety.Normalize(supplied);
                DeploymentPathSafety.RequirePlainTree(root);
                total = 0;
                if (Directory.Exists(root))
                {
                    foreach (var directory in Directory.EnumerateDirectories(root))
                    {
                        long bytes = 0;
                        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                        {
                            DeploymentPathSafety.Normalize(file);
                            bytes = checked(bytes + new FileInfo(file).Length);
                        }
                        total = checked(total.Value + bytes);
                        artifacts.Add(new(directory, bytes, "PROTECTED", "Active/recovery/lease resolution is not proven; age and a completion manifest do not authorize deletion."));
                    }
                    foreach (var file in Directory.EnumerateFiles(root)) total = checked(total.Value + new FileInfo(file).Length);
                }
                try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace; }
                catch (Exception ex) { warning = "Capacity unavailable: " + ex.Message; }
                if (free is < 1_073_741_824L) warning = "Low capacity: less than 1 GiB free. No cleanup was performed.";
            }
            catch (Exception ex)
            {
                total = null;
                warning = "Inspection incomplete; preserve all artifacts: " + ex.Message;
                artifacts.Add(new(supplied, null, "PROTECTED", warning));
            }
            capacity.Add(new(supplied, free, total, warning));
        }
        return new(true, false, minimumKeep, minimumAgeDays, artifacts.ToArray(), capacity.ToArray(), Array.Empty<string>());
    }
}
