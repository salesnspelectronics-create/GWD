using System.Diagnostics;

namespace GenericWeb.DeploymentManager.Services;

public sealed record BuildPerformanceStage(string Name, TimeSpan Elapsed, string Detail, string Status = "DONE")
{
    public bool Skipped => string.Equals(Status, "SKIPPED", StringComparison.OrdinalIgnoreCase);
}

public sealed class BuildPerformanceTrace
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly List<BuildPerformanceStage> _stages = new();
    private readonly object _gate = new();

    public BuildPerformanceTrace(string buildMode, string dependencyMode)
    {
        StartedAt = DateTime.Now;
        BuildMode = buildMode;
        DependencyMode = dependencyMode;
    }

    public DateTime StartedAt { get; }
    public DateTime? CompletedAt { get; private set; }
    public string BuildMode { get; }
    public string DependencyMode { get; }
    public TimeSpan TotalElapsed => CompletedAt.HasValue ? (CompletedAt.Value - StartedAt) : _total.Elapsed;
    public IReadOnlyList<BuildPerformanceStage> Stages { get { lock (_gate) return _stages.ToArray(); } }

    public T Measure<T>(string name, Func<T> action, string detail = "")
    {
        var sw = Stopwatch.StartNew();
        try { var result = action(); sw.Stop(); Add(name, sw.Elapsed, detail); return result; }
        catch (Exception ex) { sw.Stop(); Add(name, sw.Elapsed, AppendError(detail, ex), "FAILED"); throw; }
    }

    public void Measure(string name, Action action, string detail = "")
    {
        var sw = Stopwatch.StartNew();
        try { action(); sw.Stop(); Add(name, sw.Elapsed, detail); }
        catch (Exception ex) { sw.Stop(); Add(name, sw.Elapsed, AppendError(detail, ex), "FAILED"); throw; }
    }

    public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action, string detail = "")
    {
        var sw = Stopwatch.StartNew();
        try { var result = await action(); sw.Stop(); Add(name, sw.Elapsed, detail); return result; }
        catch (Exception ex) { sw.Stop(); Add(name, sw.Elapsed, AppendError(detail, ex), "FAILED"); throw; }
    }

    public async Task MeasureAsync(string name, Func<Task> action, string detail = "")
    {
        var sw = Stopwatch.StartNew();
        try { await action(); sw.Stop(); Add(name, sw.Elapsed, detail); }
        catch (Exception ex) { sw.Stop(); Add(name, sw.Elapsed, AppendError(detail, ex), "FAILED"); throw; }
    }

    public void AddSkipped(string name, string detail)
        => Add(name, TimeSpan.Zero, detail, "SKIPPED");

    public void Complete()
    {
        if (CompletedAt.HasValue) return;
        _total.Stop();
        CompletedAt = DateTime.Now;
    }

    private void Add(string name, TimeSpan elapsed, string detail, string status = "DONE")
    {
        lock (_gate) _stages.Add(new BuildPerformanceStage(name, elapsed, SecretRedactor.Redact(detail), status));
    }

    private static string AppendError(string detail, Exception ex) =>
        string.IsNullOrWhiteSpace(detail) ? ex.Message : detail + " Error: " + ex.Message;

    public string ToPlainText()
    {
        var stages = Stages;
        var total = TotalElapsed.TotalMilliseconds <= 0 ? 1 : TotalElapsed.TotalMilliseconds;
        var lines = new List<string>
        {
            "Generic Web Deployment Manager — Build Performance",
            $"Started: {StartedAt:yyyy-MM-dd HH:mm:ss}",
            $"Build mode: {BuildMode}",
            $"Dependency mode: {DependencyMode}",
            $"BUILD TOTAL: {TotalElapsed:hh\\:mm\\:ss\\.fff}",
            "",
            "Stage                                      Time          %    Status / Detail",
            new string('-', 100)
        };
        foreach (var s in stages.OrderByDescending(x => x.Elapsed))
        {
            var pct = s.Skipped ? 0 : (s.Elapsed.TotalMilliseconds / total) * 100.0;
            var status = s.Status;
            lines.Add($"{s.Name,-42} {s.Elapsed:hh\\:mm\\:ss\\.fff} {pct,6:0.0}%  {status}  {s.Detail}".TrimEnd());
        }
        if (stages.Count > 0)
        {
            var top = stages.Where(x => !x.Skipped).OrderByDescending(x => x.Elapsed).Take(3).ToArray();
            lines.Add("");
            lines.Add("Top time consumers:");
            for (var i = 0; i < top.Length; i++) lines.Add($"{i + 1}. {top[i].Name} — {top[i].Elapsed:hh\\:mm\\:ss\\.fff}");
        }
        return SecretRedactor.Redact(string.Join(Environment.NewLine, lines));
    }
}
