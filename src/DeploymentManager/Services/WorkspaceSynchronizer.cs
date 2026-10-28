using System.Security.Cryptography;
using System.Text.Json;

namespace GenericWeb.DeploymentManager.Services;

/// <summary>Mirrors explicit non-cache files and verifies actual bytes before build.</summary>
public static class WorkspaceSynchronizer
{
    public sealed record Result(int Added, int Updated, int Removed, int Unchanged, int Verified, int Mismatch);
    private const string Manifest = ".gwd-workspace-manifest.json";
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".worktrees", "IIS-Publish", "bin", "obj", "node_modules", ".vs", "dist", "out" };

    public static bool IsExcluded(string relative) => relative.Replace('\\', '/').Split('/').SkipLast(1).Any(ExcludedDirectories.Contains)
        || relative.Replace('\\', '/') is Manifest or ".gwd-workspace-manifest.json.tmp" or ".gwd-workspace.lock";

    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Dictionary<string, string> Scan(string root, CancellationToken ct)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.Directory) && ExcludedDirectories.Contains(Path.GetFileName(entry))) continue;
                if (IsExcluded(relative)) continue;
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Workspace sync refuses non-excluded links: " + relative);
                if (attributes.HasFlag(FileAttributes.Directory)) Visit(entry);
                else files.Add(relative, Hash(entry));
            }
        }
        Visit(root);
        return files;
    }

    public static Result Sync(string source, string workspace, Action<string> log, CancellationToken ct = default)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        workspace = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
        if (source.Equals(workspace, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || workspace.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Incoming source and workspace must be separate, non-nested directories.");
        var expected = Scan(source, ct);
        Directory.CreateDirectory(workspace);
        var actual = Scan(workspace, ct);
        int added = 0, updated = 0, removed = 0, unchanged = 0;
        foreach (var (relative, hash) in expected)
        {
            ct.ThrowIfCancellationRequested();
            if (actual.TryGetValue(relative, out var destinationHash) && hash == destinationHash)
            {
                unchanged++; log($"[WORKSPACE] Unchanged: {relative}"); continue;
            }
            var destination = Path.Combine(workspace, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(source, relative), destination, true);
            // Mark changed build inputs as newly written; an old/equal Incoming timestamp
            // must not let MSBuild reuse binaries compiled from the previous contents.
            File.SetLastWriteTimeUtc(destination, DateTime.UtcNow);
            if (actual.ContainsKey(relative)) { updated++; log($"[WORKSPACE] Updated: {relative}"); }
            else { added++; log($"[WORKSPACE] Added: {relative}"); }
        }
        foreach (var relative in actual.Keys.Where(k => !expected.ContainsKey(k)))
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(Path.Combine(workspace, relative));
            removed++; log($"[WORKSPACE] Removed: {relative}");
        }
        var verified = Verify(source, workspace, log, ct, expected);
        ct.ThrowIfCancellationRequested();
        var manifestPath = Path.Combine(workspace, Manifest);
        File.WriteAllText(manifestPath + ".tmp", JsonSerializer.Serialize(new
        {
            SchemaVersion = 2, UpdatedAtUtc = DateTime.UtcNow, SourceFiles = expected,
            Verified = verified, HashAlgorithm = "SHA256"
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(manifestPath + ".tmp", manifestPath, true);
        log($"[WORKSPACE] Added: {added}; Updated: {updated}; Removed: {removed}; Unchanged: {unchanged}; Verified: {verified}; Mismatch: 0");
        return new(added, updated, removed, unchanged, verified, 0);
    }

    public static int Verify(string source, string workspace, Action<string> log, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? expected = null)
    {
        var sources = Scan(source, ct);
        var destinations = Scan(workspace, ct);
        int verified = 0, mismatch = 0;
        foreach (var relative in sources.Keys.Union(destinations.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(expected?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            sources.TryGetValue(relative, out var sourceHash);
            destinations.TryGetValue(relative, out var workspaceHash);
            var stable = expected is null || (expected.TryGetValue(relative, out var original) && original == sourceHash);
            if (sourceHash is null || workspaceHash is null || sourceHash != workspaceHash || !stable)
            {
                mismatch++;
                log($"[WORKSPACE] Mismatch: {relative}; source SHA256={sourceHash ?? "MISSING"}; workspace SHA256={workspaceHash ?? "MISSING"}; source snapshot stable={stable}");
            }
            else { verified++; log($"[WORKSPACE] Verified: {relative}; SHA256={sourceHash}"); }
        }
        log($"[WORKSPACE] Verification: Verified: {verified}; Mismatch: {mismatch}");
        if (mismatch != 0) throw new InvalidOperationException($"Workspace SHA256 verification failed ({mismatch} mismatch). Build/Publish blocked.");
        return verified;
    }
}
