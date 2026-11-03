using GenericWeb.DeploymentManager.Services;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
async Task Fails(Func<Task> operation, string name)
{
    try { await operation(); } catch (IOException) { Check(true, name); return; }
    throw new Exception("Expected failure: " + name);
}
IisSafety Fake(Func<string, CommandResult> response, List<string> calls, List<string>? logs = null) =>
    new((c, _) => { calls.Add(c); return Task.FromResult(response(c)); }, x => logs?.Add(x));
CommandResult State(string s) => new(0, s, "");
foreach (var state in new[] { "Started", "Stopped" })
{
    var calls = new List<string>();
    var actual = await Fake(_ => State(state), calls).ValidateAsync("site", "pool", default);
    Check(actual.Site == state && actual.Pool == state && calls.Count == 2, "independent preflight " + state);
}
foreach (var state in new[] { "Unknown", "Starting", "Stopping", "", "Started\nUnknown", "ERROR" })
    await Fails(() => Fake(c => State(c.Contains("list site") ? state : "Started"), new()).ValidateAsync("site", "pool", default), "block site " + state);
await Fails(() => Fake(c => State(c.Contains("list apppool") ? "Unknown" : "Started"), new()).ValidateAsync("site", "pool", default), "block unknown pool with healthy site");
await Fails(() => Fake(_ => new(5, "", "access denied"), new()).ValidateAsync("site", "pool", default), "nonzero exit blocks");
foreach (var exit in new[] { 0, 1 })
{
    var logs = new List<string>();
    await Fails(() => Fake(_ => new(exit, "HRESULT 0x80010006", "bogus state"), new(), logs).ValidateAsync("site", "pool", default), "HRESULT blocks even zero exit " + exit);
    Check(logs.Any(x => x.Contains("management_failure=True") && x.Contains("stdout=") && x.Contains("stderr=")), "command diagnostics retained");
}
{
    var calls = new List<string>();
    await Fails(() => Fake(_ => State("Unknown"), calls).TransitionAsync("site", "site", "Stopped", default), "unknown stop blocked");
    Check(calls.Count == 1 && calls.All(x => x.StartsWith("list")), "unknown never mutates");
}
{
    var calls = new List<string>();
    await Fake(_ => State("Stopped"), calls).TransitionAsync("site", "site", "Stopped", default);
    Check(calls.Count == 1, "stopped stop idempotent");
}
foreach (var desired in new[] { "Stopped", "Started" })
{
    var calls = new List<string>(); int reads = 0;
    var fake = Fake(c => c.StartsWith("list") ? State(reads++ == 0 ? (desired == "Stopped" ? "Started" : "Stopped") : desired) : State(""), calls);
    await fake.TransitionAsync("site", "site", desired, default);
    Check(calls.Count == 3, "transition observed before and after " + desired);
}
{
    var calls = new List<string>();
    await Fails(() => Fake(c => c.StartsWith("list") ? State("Started") : new(1, "stop failed", ""), calls).TransitionAsync("site", "site", "Stopped", default), "failed stop propagates");
    Check(calls.Count == 2, "failed stop not retried");
}
{
    var root = Path.Combine(Path.GetTempPath(), "gwd-test-" + Guid.NewGuid().ToString("N"));
    var locks = Path.Combine(root, "locks"); var journals = Path.Combine(root, "attempts");
    try
    {
        using (var first = new IisDeploymentAttempt(locks, journals, new[] { "site:a", "pool:a", "path:a" }, "one", _ => { }))
        {
            await Fails(() => { using var other = new IisDeploymentAttempt(locks, journals, new[] { "site:a", "pool:b", "path:b" }, "two", _ => { }); return Task.CompletedTask; }, "same site rejects concurrent profile");
            first.Stage("PREFLIGHT", "RUNNING"); first.Stage("PREFLIGHT", "PASS");
            first.BeginMutation();
            first.Stage("STOP", "RUNNING");
            first.RecordRecovery("FILES_RESTORED / IIS_UNHEALTHY");
        }
        await Fails(() => { using var retry = new IisDeploymentAttempt(locks, journals, new[] { "site:b", "pool:a", "path:b" }, "retry", _ => { }); return Task.CompletedTask; }, "unresolved pool blocks changed site retry");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(journals, "one.json")));
        Check(doc.RootElement.GetProperty("Stages").GetProperty("STOP").GetString() == "FAILED", "failed running stage persisted");
        Check(doc.RootElement.GetProperty("Stages").GetProperty("FILE_DEPLOY").GetString() == "SKIPPED", "failed STOP skips files");
        Check(doc.RootElement.GetProperty("Recovery").GetString() == "FILES_RESTORED / IIS_UNHEALTHY", "independent file/service recovery persisted");
        Check(doc.RootElement.GetProperty("DurationMs").TryGetProperty("STOP", out _), "stage duration persisted");
        using (var clean = new IisDeploymentAttempt(locks, journals, new[] { "site:c" }, "clean", _ => { })) { clean.BeginMutation(); clean.Resolve(); }
        using var again = new IisDeploymentAttempt(locks, journals, new[] { "site:c" }, "again", _ => { });
        Check(true, "resolved attempt allows new lease");
    }
    finally { Directory.Delete(root, true); }
}
{
    var calls = new List<string>(); var logs = new List<string>();
    await IisDiagnostics.CollectAsync(command => { calls.Add(command); if (command.Contains("apppool")) throw new IOException("access denied"); return Task.CompletedTask; },
        () => throw new IOException("Event Log denied"), "site", "pool", logs.Add);
    Check(calls.Count == 3, "one diagnostic failure preserves other IIS observations");
    Check(logs.Any(x => x.Contains("Event Log unavailable")) && logs.Any(x => x.Contains("access denied")), "Event Log failure nonfatal and explicit");
}
{
    var calls = new List<string>();
    await Fails(() => Fake(c => c.StartsWith("list") ? State("Stopped") : new(1, "HRESULT 0x80010006", ""), calls).TransitionAsync("site", "site", "Started", default), "start failure propagated for rollback");
    Check(calls.Count == 2, "start management failure never blindly retried");
}
Console.WriteLine($"{passed} checks passed; no IIS or production files accessed.");
