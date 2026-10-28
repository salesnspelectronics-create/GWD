using System.Diagnostics;
using System.Text.Json;

namespace GenericWeb.DeploymentManager.Services;

public sealed record RuntimeMetricSample(DateTimeOffset At, double CpuPercent, long WorkingSetBytes, long PrivateBytes, long ManagedBytes, int ThreadCount)
{
    public string ToDisplayText() => $"CPU {CpuPercent:0.0}%  |  RAM {FormatBytes(WorkingSetBytes)}  |  Private {FormatBytes(PrivateBytes)}  |  Managed {FormatBytes(ManagedBytes)}  |  Threads {ThreadCount}";

    public static string FormatBytes(long value)
    {
        var mb = value / 1024d / 1024d;
        return mb >= 1024 ? $"{mb / 1024d:0.00} GB" : $"{mb:0.0} MB";
    }
}

public sealed class RuntimeMetricsService : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Queue<RuntimeMetricSample> _history = new();
    private readonly object _sync = new();
    private DateTimeOffset _lastAt = DateTimeOffset.Now;
    private TimeSpan _lastCpu;
    private const int MaxSamples = 720; // 1 hour at a 5-second sampling interval.

    public RuntimeMetricsService()
    {
        _lastCpu = _process.TotalProcessorTime;
    }

    public RuntimeMetricSample Sample()
    {
        try
        {
            _process.Refresh();
            var now = DateTimeOffset.Now;
            var cpuNow = _process.TotalProcessorTime;
            var elapsedMs = Math.Max(1, (now - _lastAt).TotalMilliseconds);
            var cpuMs = Math.Max(0, (cpuNow - _lastCpu).TotalMilliseconds);
            var cpu = Math.Clamp(cpuMs / elapsedMs / Math.Max(1, Environment.ProcessorCount) * 100d, 0d, 100d);
            _lastAt = now;
            _lastCpu = cpuNow;
            var sample = new RuntimeMetricSample(now, cpu, _process.WorkingSet64, _process.PrivateMemorySize64, GC.GetTotalMemory(false), _process.Threads.Count);
            lock (_sync)
            {
                _history.Enqueue(sample);
                while (_history.Count > MaxSamples) _history.Dequeue();
            }
            return sample;
        }
        catch (Exception ex)
        {
            InternalDiagnostics.Record(ex, "Runtime metrics sample");
            return new RuntimeMetricSample(DateTimeOffset.Now, 0, 0, 0, GC.GetTotalMemory(false), 0);
        }
    }

    public RuntimeMetricSample[] GetHistory()
    {
        lock (_sync) return _history.ToArray();
    }

    public string ToPlainText() => string.Join(Environment.NewLine, GetHistory().Select(x => $"{x.At:yyyy-MM-dd HH:mm:ss} | {x.ToDisplayText()}"));
    public string ToJson() => JsonSerializer.Serialize(GetHistory(), new JsonSerializerOptions { WriteIndented = true });

    public void Dispose() => _process.Dispose();
}
