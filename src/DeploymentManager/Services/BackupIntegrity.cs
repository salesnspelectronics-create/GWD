using System.Security.Cryptography;
using System.Text.Json;

namespace GenericWeb.DeploymentManager.Services;

// Completion sidecars are outside the payload and are never inferred from folder age.
public static class BackupIntegrity
{
    public sealed record Manifest(string Id, string Project, string Target, Dictionary<string, string> Inventory);

    public static Dictionary<string, string> Capture(string root)
    {
        DeploymentPathSafety.RequirePlainTree(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string directory)
        {
            DeploymentPathSafety.Normalize(directory);
            result.Add("D:" + Path.GetRelativePath(root, directory), "");
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                DeploymentPathSafety.Normalize(file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                result.Add("F:" + Path.GetRelativePath(root, file), Convert.ToHexString(SHA256.HashData(stream)));
            }
            foreach (var child in Directory.EnumerateDirectories(directory)) Visit(child);
        }
        Visit(root);
        return result;
    }

    public static void Verify(string root, Dictionary<string, string> expected)
    {
        var actual = Capture(root);
        if (actual.Count != expected.Count || expected.Any(x => !actual.TryGetValue(x.Key, out var hash) || hash != x.Value))
            throw new IOException("Backup directory/SHA256 inventory mismatch: " + root);
    }

    public static void Complete(string backup, string project, string target, Dictionary<string, string> inventory)
    {
        DeploymentPathSafety.Normalize(backup + ".complete.json");
        using var stream = new FileStream(backup + ".complete.json", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new Manifest(Guid.NewGuid().ToString("N"), project, DeploymentPathSafety.RequireApplicationDirectory(target), inventory));
        stream.Flush(flushToDisk: true);
    }

    public static Manifest Require(string backup, string project, string target)
    {
        DeploymentPathSafety.RequireSeparate(backup, target);
        DeploymentPathSafety.Normalize(backup + ".complete.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(backup + ".complete.json"));
        if (manifest is null || !Guid.TryParseExact(manifest.Id, "N", out _) || manifest.Inventory is null
            || !string.Equals(manifest.Project, project, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(DeploymentPathSafety.RequireApplicationDirectory(manifest.Target), DeploymentPathSafety.RequireApplicationDirectory(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Backup ownership/completion metadata does not match the selected project and target.");
        Verify(backup, manifest.Inventory);
        return manifest;
    }
}
