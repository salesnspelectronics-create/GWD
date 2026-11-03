using GenericWeb.DeploymentManager.CodeQuality;
using GenericWeb.DeploymentManager.Models;
using GenericWeb.DeploymentManager.Services;
using System.IO.Compression;

if (args.Contains("--fixture"))
{
    Console.WriteLine("warning CS8629: first warning token=fixture-secret");
    Console.Error.WriteLine("error TS2322: fixture error");
    for (var i = 0; i < 20000; i++) Console.WriteLine(new string('x', 100));
    Console.WriteLine("warning CS9113: last warning");
    if (args.Contains("--wait")) await Task.Delay(TimeSpan.FromMinutes(1));
    return args.Contains("--fail") ? 9 : 0;
}
var root = Path.Combine(Path.GetTempPath(), "gwd-windows-quality-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("GWDM_APPDATA_ROOT", root);
var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
try
{
    var app = new AppDataService();
    var source = Path.Combine(root, "source"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "A.cs"), "class A {}");
    var profile = new DeploymentProfile { ApplicationName = "fixture", SourceFolder = source };
    var history = new DeploymentHistoryService(); var attempt = history.BeginAttempt(profile, "");
    var exe = Environment.ProcessPath!;
    var prefix = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? "\"" + typeof(Program).Assembly.Location + "\" " : "";
    foreach (var mode in new[] { "success", "failure", "cancel", "timeout" })
    {
        var quality = new CodeQualitySession(); quality.Start(app, profile, attempt.ProjectId, mode);
        var runner = new CommandRunner { CodeQuality = quality };
        using var cts = new CancellationTokenSource();
        if (mode == "cancel") cts.CancelAfter(TimeSpan.FromSeconds(2));
        var arguments = prefix + "--fixture build" + (mode == "failure" ? " --fail" : mode is "cancel" or "timeout" ? " --wait" : "");
        var result = "Failed";
        try
        {
            var command = await runner.RunAsync(exe, arguments, source, cts.Token, mode == "timeout" ? TimeSpan.FromSeconds(2) : null);
            Check(command.ExitCode == (mode == "failure" ? 9 : 0), "unchanged exit code " + mode);
            Check(!command.Output.Contains("first warning"), "bounded CommandResult excludes early output " + mode);
            result = command.ExitCode == 0 ? "Successful" : "Failed";
        }
        catch (OperationCanceledException) when (mode == "cancel") { result = "Cancelled"; Check(true, "cancellation still propagates"); }
        catch (TimeoutException) when (mode == "timeout") { Check(true, "timeout still propagates"); }
        finally { quality.Finish(result); }
        var record = CodeQualitySession.Store(app).Read().Single(b => b.AttemptId == mode);
        Check(record.Items.Any(i => i.Diagnostic.Code == "CS8629"), "early real stdout retained " + mode);
        Check(record.Items.Any(i => i.Diagnostic.Code == "TS2322"), "real stderr retained " + mode);
        Check(!DiagnosticExport.Json(record).Contains("fixture-secret"), "real stream redaction " + mode);
    }
    var disabled = new CodeQualitySession();
    // An uninitialized collector must stay optional.
    var runnerWithoutCollector = new CommandRunner { CodeQuality = disabled };
    var unaffected = await runnerWithoutCollector.RunAsync(exe, prefix + "--fixture build", source);
    Check(unaffected.ExitCode == 0, "missing collector never blocks command");
    var qualityFolder = Path.Combine(root, "CodeQuality");
    var archived = qualityFolder + ".archived"; Directory.Move(qualityFolder, archived); File.WriteAllText(qualityFolder, "blocks store directory");
    var broken = new CodeQualitySession(); broken.Start(app, profile, attempt.ProjectId, "broken-store");
    var failOpen = await new CommandRunner { CodeQuality = broken }.RunAsync(exe, prefix + "--fixture build", source);
    broken.Finish("Successful");
    Check(failOpen.ExitCode == 0, "collector storage failure does not alter process result");
    File.Delete(qualityFolder); Directory.Move(archived, qualityFolder);
    history.CompleteAttempt(attempt, profile, "Successful", TimeSpan.FromSeconds(1), source, null);
    Check(history.GetAttempts(attempt.ProjectId).Any(a => a.AttemptId == attempt.AttemptId && a.Result == "Successful"), "history contract remains readable");
    var zip = new DiagnosticBundleService().Export(root, profile, history, "");
    using var archive = ZipFile.OpenRead(zip);
    Check(archive.Entries.Any(e => e.FullName.StartsWith("history/")) && archive.Entries.Any(e => e.FullName.StartsWith("code-quality/") && e.Name.EndsWith(".json")), "bundle retains history and adds quality");
}
finally { Environment.SetEnvironmentVariable("GWDM_APPDATA_ROOT", null); if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine($"WINDOWS INTEGRATION TESTS: {count} passed");
return 0;
