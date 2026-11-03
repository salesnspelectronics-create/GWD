using GenericWeb.DeploymentManager.CodeQuality;
using GenericWeb.DeploymentManager.Services;
using System.Text.Json;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
Diagnostic Parse(string line) => DiagnosticParser.Parse(line) ?? throw new Exception("Not parsed: " + line);
var fixtures = new (string Text, string Code, string Severity, string Category)[]
{
    (@"C:\repo\A.cs(12,5): warning CS8629: Nullable value type may be null. [C:\repo\A.csproj]", "CS8629", "Warning", "Quality"),
    (@"A.cs(3,8): warning CS9113: Parameter is unread.", "CS9113", "Warning", "Quality"),
    ("CSC : warning CS0168: Unused variable", "CS0168", "Warning", "Quality"),
    ("warning NU1902: Package has a moderate severity vulnerability", "NU1902", "Warning", "Security"),
    ("repo.csproj : warning NU1903: Package has a high severity vulnerability", "NU1903", "Warning", "Security"),
    ("src/main.ts(8,3): error TS2322: Type mismatch", "TS2322", "Error", "Quality"),
    ("error TS18003: No inputs found", "TS18003", "Error", "Quality"),
    ("(!) module.ts is dynamically imported by x but also statically imported by y", "VITE_IMPORT", "Warning", "Performance"),
    ("(!) Some chunks are larger than 500 kB after minification. Consider:", "VITE_CHUNK_SIZE", "Warning", "Performance"),
    ("warning X999: Unknown tool warning", "Unclassified", "Warning", "Unclassified"),
    ("error: unknown failure", "Unclassified", "Error", "Unclassified")
};
foreach (var fixture in fixtures)
{
    var d = Parse(fixture.Text);
    Check(d.Code == fixture.Code && d.Severity == fixture.Severity && d.Category == fixture.Category, fixture.Code + " fixture");
}
Check(Parse(fixtures[0].Text).Line == 12 && Parse(fixtures[0].Text).File == @"C:\repo\A.cs", "Windows file/line");
Check(Parse("src/main.ts:8:3 - error TS2322: Type mismatch").Line == 8, "TypeScript colon file/line");
Check(Parse(fixtures[3].Text).File is null, "warning without file/line");
Check(Parse("\u001b[33mwarning NU1903: advisory\u001b[0m").Message == "advisory", "ANSI colors removed");
Check(DiagnosticParser.Parse("Build succeeded.") is null, "normal output ignored by parser");
Check(DiagnosticParser.Parse("    0 Error(s)") is null && DiagnosticParser.Parse("    3 Warning(s)") is null, "MSBuild summary counts do not create phantom diagnostics");
Check(Parse(fixtures[4].Text).Risk == "High", "NuGet vulnerability risk classification");
var root = Path.Combine(Path.GetTempPath(), "gwd-quality-test-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new DiagnosticStore(root);
    var build = new BuildDiagnostics { ProjectId = "test-project", AttemptId = "history-attempt", BuildMode = "Full" };
    var collector = new DiagnosticCollector(build, store, SecretRedactor.Redact);
    foreach (var name in new[] { "restore", "build", "publish" })
    {
        var stage = collector.Begin("dotnet", name);
        collector.Line(stage, "stdout", fixtures[0].Text);
        collector.End(stage, 0, "Completed");
    }
    Check(build.Items.Count == 1 && build.Items[0].Occurrences.Count == 3, "one issue, three stage occurrences");
    Check(build.Items[0].Occurrences.Select(o => o.Id).Distinct().Count() == 3, "unique occurrence IDs");
    var concurrent = collector.Begin("dotnet", "build");
    Parallel.For(0, 200, n => collector.Line(concurrent, n % 2 == 0 ? "stdout" : "stderr", fixtures[0].Text));
    collector.End(concurrent, 0, "Completed");
    Check(build.Items.Count == 1 && build.Items[0].Occurrences.Count == 203, "concurrent stdout/stderr retain every occurrence");
    collector.Finish("Successful");
    Check(build.Result == "Successful" && build.Items[0].Diagnostic.Severity == "Warning", "successful build retains warning without failure");
    Check(store.Read().Single().AttemptId == "history-attempt", "history attempt correlation round trip");
    Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "atomic writes leave no temporary files");
    var second = new BuildDiagnostics { ProjectId = "test-project", AttemptId = "failed-attempt" };
    collector = new DiagnosticCollector(second, store, SecretRedactor.Redact);
    var failed = collector.Begin("npm", "run build");
    collector.Line(failed, "stderr", "error TS2322: incomplete output");
    collector.Line(failed, "stderr", "  additional detail on the next line");
    collector.End(failed, 1, "Completed"); collector.Finish("Failed");
    Check(second.Result == "Failed" && second.StageCoverage[0].ExitCode == 1 && second.Items[0].Occurrences[0].Context.Count == 1, "failed partial multiline output");
    Check(second.StageCoverage.Any(s => s.Name == "restore" && s.Status == "Skipped"), "skipped stage coverage");
    var third = new BuildDiagnostics { ProjectId = "test-project", AttemptId = "cancelled-attempt" };
    collector = new DiagnosticCollector(third, store, SecretRedactor.Redact);
    var cancelled = collector.Begin("dotnet", "build --password=hidden-password");
    SecretRedactor.Register("very-sensitive-value");
    collector.Line(cancelled, "stdout", "warning NU1903: token=very-sensitive-value");
    for (var i = 0; i < 100000; i++) collector.Line(cancelled, "stdout", "normal output " + i);
    collector.Line(cancelled, "stdout", "warning CS9113: final warning after large output");
    collector.End(cancelled, null, "Cancelled"); collector.Finish("Cancelled");
    Check(third.Items.Count == 2 && third.StageCoverage[0].Status == "Cancelled", "cancellation preserves first and last diagnostic across large output");
    var persisted = File.ReadAllText(Path.Combine(root, third.BuildId + ".json"));
    var evidence = string.Join("", Directory.EnumerateFiles(Path.Combine(root, third.BuildId)).Select(File.ReadAllText));
    Check(!persisted.Contains("very-sensitive-value") && !persisted.Contains("hidden-password") && !evidence.Contains("very-sensitive-value"), "redaction before JSON and evidence persistence");
    var unfinished = new BuildDiagnostics { ProjectId = "test-project", AttemptId = "interrupted-attempt" };
    collector = new DiagnosticCollector(unfinished, store, SecretRedactor.Redact);
    var interrupted = collector.Begin("dotnet", "publish"); collector.Line(interrupted, "stdout", "warning CS8629: interrupted warning");
    Check(store.Read().Single(b => b.BuildId == unfinished.BuildId).Items.Count == 1, "interrupted attempt checkpoint exists before Finish");
    collector.Finish("Interrupted");
    Check(unfinished.StageCoverage[0].Status == "Interrupted", "unfinished stage finalized as interrupted");
    Check(JsonDocument.Parse(DiagnosticExport.Json(third)).RootElement.GetProperty("SchemaVersion").GetInt32() == 1 && DiagnosticExport.Handoff(third).Contains("CS9113"), "JSON and handoff export");
    var retried = new BuildDiagnostics { ProjectId = "test-project", AttemptId = "retry" };
    collector = new DiagnosticCollector(retried, store, SecretRedactor.Redact);
    var first = collector.Begin("dotnet", "publish"); collector.End(first, 1, "Completed");
    var retry = collector.Begin("dotnet", "publish"); collector.End(retry, 0, "Completed");
    collector.Finish("Failed");
    Check(retried.Result == "Successful" && retried.DeploymentResult == "Failed", "successful build retry is independent of later deployment failure");
    store.Retain(2); Check(store.Read().Count() == 2, "retention limits build records");
    File.WriteAllText(Path.Combine(root, "corrupt.json"), "bad JSON");
    Check(store.Read().Count() == 2, "corrupt record does not hide valid builds");
    var badRoot = Path.Combine(root, "file-not-directory"); File.WriteAllText(badRoot, "file");
    try { new DiagnosticStore(badRoot).Save(new()); Check(false, "store failure surfaced to fail-open adapter"); }
    catch (IOException) { Check(true, "store failure surfaced to fail-open adapter"); }
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine($"CODE QUALITY TESTS: {passed} passed");
