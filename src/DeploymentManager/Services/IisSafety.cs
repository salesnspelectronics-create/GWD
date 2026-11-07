namespace GenericWeb.DeploymentManager.Services;

// Shared by the real appcmd adapter and the fake-runner regression harness.
public sealed class IisSafety
{
    private readonly Func<string, CancellationToken, Task<CommandResult>> _run;
    private readonly Action<string> _log;
    public IisSafety(Func<string, CancellationToken, Task<CommandResult>> run, Action<string> log)
    { _run = run; _log = log; }

    public static string ParseState(CommandResult result)
    {
        var text = result.Output.Trim();
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Error)) return "Unknown";
        return text is "Started" or "Stopped" or "Starting" or "Stopping" ? text : "Unknown";
    }

    public async Task<CommandResult> CheckedAsync(string command, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        try
        {
            var result = await _run(command, ct);
            var text = result.Output + "\n" + result.Error;
            var hresult = System.Text.RegularExpressions.Regex.Match(text, @"0x[0-9a-fA-F]{8}").Value;
            if (string.IsNullOrEmpty(hresult) && result.ExitCode < 0) hresult = $"0x{unchecked((uint)result.ExitCode):X8}";
            var managementFailure = text.Contains("0x80010006", StringComparison.OrdinalIgnoreCase)
                || text.Contains("bogus state", StringComparison.OrdinalIgnoreCase);
            _log($"[IIS] command={command}; exit={result.ExitCode}; HRESULT={hresult}; management_failure={managementFailure}; elapsed_ms={(DateTime.UtcNow-started).TotalMilliseconds:F0}; stdout={result.Output}; stderr={result.Error}");
            if (result.ExitCode != 0 || managementFailure || !string.IsNullOrWhiteSpace(result.Error))
                throw new IOException($"IIS_MANAGEMENT_FAILURE: {command}; exit={result.ExitCode}; HRESULT={hresult}; {text}");
            return result;
        }
        catch (Exception ex)
        {
            _log($"[IIS] FAILED command={command}; exception_hresult=0x{ex.HResult:X8}; {ex.Message}");
            throw;
        }
    }

    private static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
            throw new ArgumentException("Invalid IIS target name.");
        return name;
    }

    public async Task<string> StateAsync(string kind, string name, CancellationToken ct)
    {
        var result = await CheckedAsync($"list {kind} /name:\"{Name(name)}\" /text:state", ct);
        var state = ParseState(result);
        _log($"[IIS] {kind}={name}; state={state}");
        return state;
    }

    public async Task<(string Site, string Pool)> ValidateAsync(string site, string pool, CancellationToken ct)
    {
        // Both observations are independent; a running pool cannot validate a site.
        var s = await StateAsync("site", site, ct);
        var p = await StateAsync("apppool", pool, ct);
        if (s is not ("Started" or "Stopped") || p is not ("Started" or "Stopped"))
            throw new IOException($"IIS_PREFLIGHT_FAILED: site={s}; apppool={p}. Deployment blocked before backup/file changes. Resolve IIS management/state manually, then retry. No automatic IIS reset or site recreation is permitted.");
        return (s, p);
    }

    public async Task RequireExclusivePoolAsync(string site, string pool, CancellationToken ct)
    {
        var result = await CheckedAsync($"list app /apppool.name:\"{Name(pool)}\" /text:app.name", ct);
        var applications = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).ToArray();
        if (applications.Length != 1
            || !string.Equals(applications[0], Name(site) + "/", StringComparison.OrdinalIgnoreCase))
            throw new IOException("IIS_SHARED_APPPOOL_SAFETY_BLOCK: selected pool must belong exclusively to the selected root application. No stop was performed.");
    }

    public async Task TransitionAsync(string kind, string name, string desired, CancellationToken ct)
    {
        var before = await StateAsync(kind, name, ct);
        if (before == desired) return; // Idempotent; no repeated stop/start.
        if (before is not ("Started" or "Stopped"))
            throw new IOException($"IIS_MANAGEMENT_STATE_INVALID: {kind}={name}; before={before}; requested={desired}. No action taken.");
        var verb = desired == "Stopped" ? "stop" : "start";
        await CheckedAsync($"{verb} {kind} /{(kind == "site" ? "site.name" : "apppool.name")}:\"{Name(name)}\"", ct);
        // Bounded polling is only for observed transitional states, never command failures.
        for (var i = 0; i < 10; i++)
        {
            var after = await StateAsync(kind, name, ct);
            if (after == desired) return;
            if (after is not ("Starting" or "Stopping"))
                throw new IOException($"IIS_TRANSITION_FAILED: {kind}={name}; before={before}; after={after}; expected={desired}");
            await Task.Delay(500, ct);
        }
        throw new IOException($"IIS_TRANSITION_TIMEOUT: {kind}={name}; expected={desired}");
    }
}
