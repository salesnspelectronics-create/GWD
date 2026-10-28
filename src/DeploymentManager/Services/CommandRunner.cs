using System.Diagnostics;
using System.Text;

namespace GenericWeb.DeploymentManager.Services;

public sealed class CommandRunner
{
    public event Action<string>? Output;
    public void Report(string message) => Output?.Invoke(SecretRedactor.Redact(message));

    public async Task<CommandResult> RunAsync(string fileName, string arguments, string? workingDirectory = null, CancellationToken ct = default, TimeSpan? timeout = null, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var cwd = workingDirectory ?? Environment.CurrentDirectory;
        Report($">>> {fileName} {arguments}");
        Report($"Working directory: {cwd}");

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = cwd
        };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null) psi.Environment.Remove(pair.Key);
                else psi.Environment[pair.Key] = pair.Value;
            }
        }
        if (Path.GetFileName(fileName).StartsWith("npm", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(fileName).StartsWith("node", StringComparison.OrdinalIgnoreCase))
        {
            // Keep enough memory available for Windows, IIS, the browser and dotnet.  V46's
            // fixed 8 GB ceiling was unsafe on machines with limited RAM/commit availability.
            var heapMb = GetSafeNodeHeapMb();
            psi.Environment["NODE_OPTIONS"] = $"--max-old-space-size={heapMb}";
            Report($"[NODE] Adaptive memory limit: {heapMb} MB (NODE_OPTIONS). Maximum allowed: 4096 MB.");
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        const int MaxCapturedChars = 256_000;
        var lastOutputUtc = DateTime.UtcNow;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lastOutputUtc = DateTime.UtcNow;
                AppendBounded(stdout, e.Data, MaxCapturedChars);
                Report(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lastOutputUtc = DateTime.UtcNow;
                AppendBounded(stderr, e.Data, MaxCapturedChars);
                Report("ERR: " + e.Data);
            }
        };

        var started = DateTime.UtcNow;
        if (!process.Start()) throw new InvalidOperationException($"Could not start {fileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.Token.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(10), heartbeatCts.Token); }
                catch (OperationCanceledException) { break; }
                if (process.HasExited) break;
                var elapsed = DateTime.UtcNow - started;
                var quiet = DateTime.UtcNow - lastOutputUtc;
                Report($"RUNNING: {Path.GetFileName(fileName)} — elapsed {elapsed.ToString(@"hh\:mm\:ss")}; last process output {quiet.TotalSeconds:0}s ago.");
            }
        }, heartbeatCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
            process.WaitForExit(); // flush async stdout/stderr events
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Kill timed-out process tree"); }
            var configuredTimeout = timeout ?? TimeSpan.Zero;
            var message = $"Process timed out after {configuredTimeout.TotalMinutes:0.#} minute(s): {fileName} {arguments}";
            Report("TIMEOUT: " + message);
            throw new TimeoutException(message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Kill cancelled process tree"); }
            Report($"CANCELLED: {Path.GetFileName(fileName)} process tree was stopped by the operator.");
            throw;
        }

        heartbeatCts.Cancel();
        try { await heartbeatTask; } catch (OperationCanceledException) { }

        var duration = DateTime.UtcNow - started;
        Report($"<<< Exit code {process.ExitCode}; duration {duration.TotalSeconds:0.0}s");
        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void AppendBounded(StringBuilder buffer, string line, int maxChars)
    {
        buffer.AppendLine(line);
        if (buffer.Length <= maxChars) return;
        var remove = buffer.Length - (maxChars / 2);
        buffer.Remove(0, remove);
        buffer.Insert(0, "[... earlier process output omitted from in-memory buffer; full output remains in the deployment log ...]\r\n");
    }

    private static int GetSafeNodeHeapMb()
    {
        const int minimumMb = 1536;
        const int maximumMb = 4096;

        // An administrator can lower the value for a constrained deployment machine.  The
        // upper bound is intentional: an override must not recreate the V46 system-pressure bug.
        var configured = Environment.GetEnvironmentVariable("GWDM_NODE_HEAP_MB");
        if (int.TryParse(configured, out var configuredMb))
            return Math.Clamp(configuredMb, minimumMb, maximumMb);

        var availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (availableBytes <= 0)
            return 2048;

        var availableMb = availableBytes / 1024L / 1024L;
        var adaptiveMb = availableMb * 30L / 100L;
        return (int)Math.Clamp(adaptiveMb, minimumMb, maximumMb);
    }

    public async Task<CommandResult> RunCheckedAsync(string fileName, string arguments, string? workingDirectory = null, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var result = await RunAsync(fileName, arguments, workingDirectory, ct, timeout);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} exited with code {result.ExitCode}.\r\n{result.Error}\r\n{result.Output}");
        return result;
    }

    public async Task<int> RunInteractiveAsync(string fileName, string arguments = "", string? workingDirectory = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}

public sealed record CommandResult(int ExitCode, string Output, string Error);
