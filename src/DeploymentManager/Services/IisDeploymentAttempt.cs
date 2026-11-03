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
    public bool FilesChanged { get; set; }
    public bool MutationStarted { get; private set; }
    public bool Resolved { get; private set; }
    public IisDeploymentAttempt(string lockRoot, string journalRoot, IEnumerable<string> targets, string id, Action<string> log)
    {
        Id = id; _log = log;
        Directory.CreateDirectory(lockRoot);
        Directory.CreateDirectory(journalRoot);
        var keys = targets.Select(x => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.ToUpperInvariant())))).Distinct().Order().ToArray();
        _unresolved = keys.Select(key => Path.Combine(lockRoot, key + ".unresolved")).ToArray();
        _journal = Path.Combine(journalRoot, id + ".json");
        try
        {
            foreach (var key in keys)
                _locks.Add(new FileStream(Path.Combine(lockRoot, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            // Check every resource: changing a profile name must not bypass a failed target.
            foreach (var key in keys)
                if (File.Exists(Path.Combine(lockRoot, key + ".unresolved")))
                    throw new IOException("IIS_UNRESOLVED_ATTEMPT: review previous attempt, backup, IIS states and HTTP health before manually clearing " + Path.Combine(lockRoot, key + ".unresolved"));
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
        // Write-ahead marker survives application termination, including a partial STOP.
        foreach (var path in _unresolved) File.WriteAllText(path, Id);
        MutationStarted = true;
        Save();
    }
    public void Resolve()
    {
        foreach (var path in _unresolved) if (File.Exists(path)) File.Delete(path);
        Resolved = true;
        Save();
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
                if (_states[stage] is "PENDING" or "RUNNING") Stage(stage, _states[stage] == "RUNNING" ? "FAILED" : "SKIPPED");
        }
        finally { IsDisposed = true; foreach (var item in _locks) item.Dispose(); }
    }
}
