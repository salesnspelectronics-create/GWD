using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GenericWeb.DeploymentManager.Services;

public sealed class IisDeploymentAttempt : IDisposable
{
    private readonly List<FileStream> _locks = new();
    private readonly Action<string> _log;
    private readonly string _journal;
    private readonly string[] _unresolved;
    private readonly Dictionary<string, string> _states = new();
    private readonly Dictionary<string, DateTime> _starts = new();
    private readonly Dictionary<string, double> _durations = new();
    private readonly List<string> _observations = new();
    public bool IsDisposed { get; private set; }
    public string Recovery { get; private set; } = "NOT_ATTEMPTED";
    public void Observe(string text) { if (IsDisposed) return; _observations.Add(text); Save(); }
    public void RecordRecovery(string result) { Recovery = result; Save(); }
    public string Id { get; }
    private bool _filesChanged;
    public bool FilesChanged
    {
        get => _filesChanged;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_filesChanged == value) return;
            AssertOwnership();
            _filesChanged = value;
            // Persist the conservative write-ahead state before the caller swaps files.
            // On persistence failure the caller must abort; do not reset this evidence.
            Save();
        }
    }
    public bool MutationStarted { get; private set; }
    public bool Resolved { get; private set; }
    public IisDeploymentAttempt(string lockRoot, string journalRoot, IEnumerable<string> targets, string id, Action<string> log, string? contentRoot = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid IIS attempt identifier.", nameof(id));
        Id = id; _log = log;
        Directory.CreateDirectory(lockRoot);
        Directory.CreateDirectory(journalRoot);
        var keys = targets.Select(x => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.ToUpperInvariant())))).Distinct().Order().ToArray();
        var lockPaths = keys.Select(key => Path.Combine(lockRoot, key + ".lock")).ToList();
        var markers = keys.Select(key => Path.Combine(lockRoot, key + ".unresolved")).ToList();
        if (contentRoot is not null)
        {
            // A sibling survives directory swaps and is the same filesystem object for
            // all operators accessing this content root, including a shared UNC target.
            var target = DeploymentPathSafety.RequireExclusiveLocalTarget(contentRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            lockPaths.Add(target + ".gwd.lock");
            markers.Add(target + ".gwd.unresolved");
            // Conservatively serialize the whole filesystem root. Unlike a child
            // sentinel, this lease cannot move when a deployment tree is swapped.
            // Access denied is a failed preflight, never permission to omit the lease.
            var volumeRoot = Path.GetPathRoot(Path.GetFullPath(contentRoot))!;
            lockPaths.Add(Path.Combine(volumeRoot, ".gwd-content.lock"));
            markers.Add(Path.Combine(volumeRoot, ".gwd-content.unresolved"));
        }
        _unresolved = markers.ToArray();
        _journal = Path.Combine(journalRoot, id + ".json");
        try
        {
            foreach (var path in lockPaths)
            {
                DeploymentPathSafety.Normalize(path);
                var lease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                _locks.Add(lease);
                lease.SetLength(0);
                var owner = Encoding.UTF8.GetBytes($"attempt={Id}; acquiredUtc={DateTime.UtcNow:O}; journal={_journal}");
                lease.Write(owner);
                lease.Flush(flushToDisk: true);
            }
            // Check every resource: changing a profile name must not bypass a failed target.
            foreach (var path in _unresolved)
            {
                DeploymentPathSafety.Normalize(path);
                if (File.Exists(path))
                    throw new IOException("IIS_UNRESOLVED_ATTEMPT: review previous attempt, backup, IIS states and HTTP health before manually clearing " + path);
            }
            if (contentRoot is not null)
            {
                var target = DeploymentPathSafety.RequireExclusiveLocalTarget(contentRoot);
                DeploymentPathSafety.RequirePlainTree(target);
                // Pre-volume-lease attempts left sibling markers. A parent or child
                // target must not bypass that historical recovery protection.
                for (var ancestor = target; !string.IsNullOrEmpty(Path.GetDirectoryName(ancestor)); ancestor = Path.GetDirectoryName(ancestor)!)
                    if (File.Exists(ancestor + ".gwd.unresolved"))
                        throw new IOException("Ancestor recovery marker blocks deployment: " + ancestor);
                if (Directory.Exists(target) && Directory.EnumerateFiles(target, "*.gwd.unresolved", SearchOption.AllDirectories).Any())
                    throw new IOException("Nested recovery marker blocks deployment: " + target);
            }
            foreach (var stage in new[] { "PREFLIGHT", "BUILD", "BACKUP", "STOP", "FILE_DEPLOY", "CONFIGURE", "START", "HEALTH", "COMMIT" })
                _states[stage] = "PENDING";
            Save();
        }
        catch { foreach (var item in _locks) item.Dispose(); throw; }
    }
    public void Stage(string stage, string state)
    {
        if (IsDisposed) return;
        _states[stage] = state;
        if (state == "RUNNING") _starts[stage] = DateTime.UtcNow;
        var elapsed = _starts.TryGetValue(stage, out var start) ? (DateTime.UtcNow - start).TotalMilliseconds : 0;
        _durations[stage] = elapsed;
        _log($"[DEPLOY] attempt={Id}; {stage}={state}; elapsed_ms={elapsed:F0}");
        Save();
    }
    public void BeginMutation()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        AssertOwnership();
        if (Resolved) throw new IOException("A resolved IIS attempt cannot begin another mutation.");
        if (MutationStarted) return;
        // Write-ahead marker survives application termination, including a partial STOP.
        foreach (var path in _unresolved)
        {
            DeploymentPathSafety.Normalize(path);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(Encoding.UTF8.GetBytes(Id));
            stream.Flush(flushToDisk: true);
        }
        MutationStarted = true;
        Save();
    }
    public void Resolve()
    {
        AssertOwnership();
        foreach (var path in _unresolved)
        {
            DeploymentPathSafety.Normalize(path);
            if (MutationStarted && !Resolved && !File.Exists(path))
                throw new IOException("Recovery marker is missing; automatic resolution blocked: " + path);
            if (File.Exists(path) && File.ReadAllText(path) != Id)
                throw new IOException("Recovery marker belongs to another attempt; it was not removed: " + path);
        }
        // Persist resolution before clearing write-ahead recovery markers.
        // A failed journal write must not remove the protection against a new mutation.
        Resolved = true;
        try { Save(); }
        catch { Resolved = false; throw; }
        // Locks stay open until Dispose. Never delete lock files or expire another lease.
        foreach (var path in _unresolved)
            if (File.Exists(path) && File.ReadAllText(path) == Id) File.Delete(path);
    }
    public void AssertOwnership()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_locks.Count == 0 || _locks.Any(x => x.SafeFileHandle.IsClosed || x.SafeFileHandle.IsInvalid))
            throw new IOException("Deployment exclusive lease is no longer owned.");
        if (MutationStarted && !Resolved)
            foreach (var path in _unresolved)
            {
                DeploymentPathSafety.Normalize(path);
                if (!File.Exists(path) || File.ReadAllText(path) != Id)
                    throw new IOException("Deployment recovery marker ownership is no longer established: " + path);
            }
    }
    private void Save()
    {
        var temporary = _journal + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new { AttemptId = Id, UpdatedUtc = DateTime.UtcNow, MutationStarted, FilesChanged, Resolved, Recovery, Stages = _states, DurationMs = _durations, Observations = _observations });
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _journal, overwrite: true);
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        try
        {
            foreach (var stage in _states.Keys.ToArray())
            {
                if (_states[stage] is not ("PENDING" or "RUNNING")) continue;
                try { Stage(stage, _states[stage] == "RUNNING" ? "FAILED" : "SKIPPED"); }
                catch (Exception ex) { RecordDisposeFailure(ex); }
            }
        }
        finally
        {
            IsDisposed = true;
            foreach (var item in _locks)
            {
                try { item.Dispose(); }
                catch (Exception ex) { RecordDisposeFailure(ex); }
            }
        }
    }
    private static void RecordDisposeFailure(Exception error)
    {
        // Journal/diagnostic failures during stack unwinding must never replace
        // the primary deployment exception, nor prevent other leases closing.
        try { InternalDiagnostics.Record(error, "Dispose IIS attempt; recovery markers retained"); }
        catch { }
    }
}

