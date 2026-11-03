namespace GenericWeb.DeploymentManager.Services;

public static class IisDiagnostics
{
    // A failed optional observation cannot replace the deployment/rollback error.
    public static async Task CollectAsync(Func<string, Task> readIis, Func<Task<CommandResult>> readEvents,
        string site, string pool, Action<string> log)
    {
        foreach (var command in new[] { $"list site /name:\"{site}\"", $"list apppool /name:\"{pool}\"", $"list vdir /vdir.name:\"{site}/\" /text:physicalPath" })
        {
            try { await readIis(command); }
            catch (Exception ex) { log("[DIAGNOSTICS] Read-only IIS observation failed: " + ex.Message); }
        }
        try
        {
            var result = await readEvents();
            log($"[DIAGNOSTICS] WAS/W3SVC read-only; exit={result.ExitCode}; stdout={result.Output}; stderr={result.Error}");
        }
        catch (Exception ex) { log("[DIAGNOSTICS] Event Log unavailable: " + ex.Message); }
    }
}
