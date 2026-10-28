using System.IO.Compression;
using System.Text.Json;
using GenericWeb.DeploymentManager.Models;
using GenericWeb.DeploymentManager.Services;
using Microsoft.Data.SqlClient;

namespace GenericWeb.DeploymentManager.SmokeTests;

internal static class SqlStrategyTests
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
        _passed++;
    }
    private static DeploymentProfile Profile(bool windows = true) => new()
    {
        SqlInstance = "test-instance", DatabaseName = "TestDatabase", UseWindowsAuthentication = windows,
        SqlUserName = "configured-user", SqlPassword = "Synthetic;quoted'\" secret-71!"
    };
    private static async Task<Exception> Fails(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) { return ex; }
        throw new InvalidOperationException("Expected operation to fail");
    }
    public static async Task RunAsync(string testRoot)
    {
        var attempts = new List<bool>();
        var messages = new List<string>();
        var p = Profile();
        var strategy = new SqlConnectionStrategy(messages.Add, s => [s, "alternate-endpoint"], (c, ct) =>
        {
            var cs = new SqlConnectionStringBuilder(c.ConnectionString);
            attempts.Add(cs.IntegratedSecurity);
            if (cs.IntegratedSecurity) throw new InvalidOperationException("Login is from an untrusted domain and cannot be used with Integrated authentication");
            return Task.CompletedTask;
        });
        using (await strategy.OpenAsync(p, "master")) { }
        Check(attempts.SequenceEqual(new[] { true, false }), "Untrusted-domain failure falls back once to configured SQL credential, before alternate endpoints");
        Check(strategy.State(p).Verified && !strategy.State(p).Integrated && !strategy.State(p).DatabaseVerified, "Fallback records effective SQL auth; master verification does not claim database verified");
        using (await strategy.OpenAsync(p, p.DatabaseName)) { }
        using (await strategy.OpenAsync(p, "master")) { }
        Check(attempts.SequenceEqual(new[] { true, false, false, false }), "Subsequent database and health connections never retry hidden Integrated authentication");
        Check(strategy.State(p).DatabaseVerified, "Database open records actual catalog verification");
        var built = new SqlConnectionStringBuilder(strategy.BuildConnectionString(p));
        Check(!built.IntegratedSecurity && built.UserID == p.SqlUserName && built.Password == p.SqlPassword && !built.PersistSecurityInfo,
            "Application connection string uses successful auth and exact configured credential");
        var fingerprint = strategy.Fingerprint(p);
        p.SiteName = "Changed unrelated IIS site";
        Check(strategy.State(p).Verified && strategy.Fingerprint(p) == fingerprint, "Unrelated settings and state reads preserve verification");
        foreach (var change in new Action<DeploymentProfile>[] {
            x => x.SqlInstance += "2", x => x.DatabaseName += "2", x => x.UseWindowsAuthentication = !x.UseWindowsAuthentication,
            x => x.SqlUserName += "2", x => x.SqlPassword += "2" })
        {
            var profile = Profile(false);
            using (await strategy.OpenAsync(profile, "master")) { }
            var before = strategy.Fingerprint(profile);
            change(profile);
            Check(!strategy.State(profile).Verified && strategy.Fingerprint(profile) != before, "Configuration change invalidates verification: " + (_passed - 5));
        }
        var calls = 0;
        var failure = new SqlConnectionStrategy(messages.Add, s => [s], (_, _) => { calls++; throw new InvalidOperationException("untrusted domain"); });
        var absent = Profile(); absent.SqlPassword = "";
        await Fails(async () => { using var c = await failure.OpenAsync(absent, "master"); });
        Check(calls == 1 && !failure.State(absent).Verified, "Absent credentials never trigger guessed fallback");
        calls = 0;
        var suggested = Profile(); suggested.SqlCredentialConfigured = false;
        await Fails(async () => { using var c = await failure.OpenAsync(suggested, "master"); });
        Check(calls == 1, "Generated but unapplied password is ineligible for fallback");
        suggested.UseWindowsAuthentication = false; calls = 0;
        await Fails(async () => { using var c = await failure.OpenAsync(suggested, "master"); });
        Check(calls == 0, "Generated but unapplied password is ineligible for direct SQL authentication");
        calls = 0;
        var network = new SqlConnectionStrategy(messages.Add, s => [s], (_, _) => { calls++; throw new TimeoutException("network timed out"); });
        await Fails(async () => { using var c = await network.OpenAsync(Profile(), "master"); });
        Check(calls == 1, "Network failure does not switch authentication");
        calls = 0;
        var failBoth = new SqlConnectionStrategy(messages.Add, s => [s], (c, _) =>
        {
            calls++; throw new InvalidOperationException(new SqlConnectionStringBuilder(c.ConnectionString).IntegratedSecurity ? "untrusted domain" : "SQL login rejected");
        });
        var both = Profile();
        var bothError = await Fails(async () => { using var c = await failBoth.OpenAsync(both, "master"); });
        Check(calls == 2 && !failBoth.State(both).Verified && bothError.Message.Contains("SQL Authentication fallback"), "Failed fallback leaves unverified state and identifies both auth attempts");
        var cancel = false;
        var cancelled = new SqlConnectionStrategy(messages.Add, s => [s], (_, _) => cancel ? Task.FromCanceled(new CancellationToken(true)) : Task.CompletedTask);
        var cancelProfile = Profile(false);
        using (await cancelled.OpenAsync(cancelProfile, "master")) { }
        cancel = true;
        var cancelError = await Fails(async () => { using var c = await cancelled.OpenAsync(cancelProfile, "master"); });
        Check(cancelError is OperationCanceledException && cancelled.State(cancelProfile).Verified, "Cancellation does not invalidate a previously verified connection");
        strategy.Invalidate(p, "Real SQL operation failure");
        Check(!strategy.State(p).Verified && !strategy.State(p).DatabaseVerified && !strategy.State(p).Integrated, "Real operation failure invalidates verification while retaining successful auth selection");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new SqlConnectionStrategy(messages.Add, s => [s], async (_, _) => { entered.SetResult(); await release.Task; });
        var stale = Profile(false);
        var pending = delayed.OpenAsync(stale, "master");
        await entered.Task;
        stale.DatabaseName = "ChangedDuringOpen";
        release.SetResult();
        var staleError = await Fails(async () => { using var c = await pending; });
        Check(staleError is OperationCanceledException && !delayed.State(stale).Verified, "In-flight result cannot verify edited configuration");
        var concurrentAttempts = new List<bool>();
        var concurrent = new SqlConnectionStrategy(messages.Add, s => [s], async (c, ct) =>
        {
            var integrated = new SqlConnectionStringBuilder(c.ConnectionString).IntegratedSecurity;
            concurrentAttempts.Add(integrated); await Task.Delay(5, ct);
            if (integrated) throw new InvalidOperationException("untrusted domain");
        });
        var cp = Profile();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ => { using var c = await concurrent.OpenAsync(cp, "master"); }));
        Check(concurrentAttempts.Count(x => x) == 1 && concurrentAttempts.Count == 9, "Concurrent health and deployment opens share one serialized fallback decision");
        var serialized = JsonSerializer.Serialize(p);
        Check(!serialized.Contains("SqlPassword") && !serialized.Contains("RemotePassword") && !serialized.Contains("SqlCredentialConfigured"), "Profile serialization excludes credentials and session authorization state");
        var legacy = JsonSerializer.Deserialize<DeploymentProfile>("{\"SqlPassword\":\"legacy-plaintext\"}")!;
        Check(legacy.SqlPassword == "" && !legacy.SqlCredentialConfigured, "Legacy plaintext profile credentials are ignored");
        var secret = p.SqlPassword;
        var variants = new[] { secret, JsonSerializer.Serialize(secret)[1..^1], secret.Replace("'", "''"), Uri.EscapeDataString(secret) };
        foreach (var variant in variants) Check(!SecretRedactor.Redact("provider detail " + variant).Contains(variant), "Redacts registered raw/escaped secret variant");
        Check(!SecretRedactor.Redact("Password=\"unknown; quoted value\";Database=db").Contains("quoted value"), "Redacts legacy quoted semicolon password");
        Check(!SecretRedactor.Redact("{\"SqlPassword\":\"unknown\\\" json secret\"}").Contains("json secret"), "Redacts legacy JSON password with escaped quote");
        var data = new AppDataService();
        var log = new LogService(data);
        log.Write("bare secret " + secret);
        InternalDiagnostics.Record(new Exception("provider exposed " + secret), "SQL");
        var report = data.SaveErrorReport("failure " + secret);
        data.SaveProjectProfile(p);
        Check(!File.ReadAllText(log.LogFile).Contains(secret) && !File.ReadAllText(report).Contains(secret)
            && Directory.GetFiles(data.InternalDiagnosticsFolder).All(f => !File.ReadAllText(f).Contains(secret)), "Disk log, error report and internal diagnostics redact before writing");
        var rawLog = Path.Combine(testRoot, "legacy.log"); File.WriteAllText(rawLog, "Password=\"old; unregistered value\";\n" + secret);
        var bundle = new DiagnosticBundleService().ExportSectionToFile(Path.Combine(testRoot, "diagnostic.zip"), DiagnosticExportSection.CurrentLog,
            p, new DeploymentHistoryService(), rawLog, null, null);
        using (var zip = ZipFile.OpenRead(bundle))
        using (var reader = new StreamReader(zip.GetEntry("logs/current-session.log")!.Open()))
        {
            var content = reader.ReadToEnd();
            Check(!content.Contains(secret) && !content.Contains("unregistered value"), "Diagnostic ZIP sanitizes historical and current secrets");
        }
        Check(messages.Any(m => m.Contains("Windows Authentication failed")) && messages.Any(m => m.Contains("fallback")) && messages.All(m => !m.Contains(secret)),
            "Auth logs identify failed/used/fallback methods without secrets");
        var runner = new CommandRunner();
        var sql = new SqlServerService(runner, new PrerequisiteService(runner), strategy);
        await sql.TestConnectionWithSafeFallbackAsync(p);
        await sql.TestConnectionAsync(p);
        Check(!attempts[^1] && !attempts[^2] && strategy.State(p).Verified, "Manual/automatic and health service APIs reuse the successful auth strategy");
        var clone = JsonSerializer.Deserialize<DeploymentProfile>(JsonSerializer.Serialize(p))!;
        clone.SqlPassword = p.SqlPassword;
        typeof(SqlConnectionStrategy).GetMethod("ShareSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(strategy, [p, clone]);
        Check(ReferenceEquals(strategy.State(p), strategy.State(clone)), "Workspace profile clone shares exact verification session");
        var deploy = new DeploymentService(runner, sql, data);
        var output = Path.Combine(testRoot, "publish"); Directory.CreateDirectory(output);
        foreach (var file in new[] { "appsettings.json", "appsettings.Production.json" })
            File.WriteAllText(Path.Combine(output, file), "{\"Keep\":\"unchanged\",\"ConnectionStrings\":{\"DefaultConnection\":\"Server=old;Integrated Security=true\"}}");
        deploy.WriteConnectionString(output, p);
        foreach (var file in new[] { "appsettings.json", "appsettings.Production.json" })
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, file)));
            var cs = new SqlConnectionStringBuilder(settings.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString());
            Check(!cs.IntegratedSecurity && cs.Password == p.SqlPassword && settings.RootElement.GetProperty("Keep").GetString() == "unchanged",
                "Effective SQL auth written without altering unrelated application settings: " + file);
        }
        Check(ProductInfo.Version == "72.0.0", "Built assembly version is 72.0.0");
        Console.WriteLine($"SQL STRATEGY TESTS: {_passed} passed. Transport is simulated; no SQL Server/IIS runtime acceptance is claimed.");
    }
}
