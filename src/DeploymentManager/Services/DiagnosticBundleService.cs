using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenericWeb.DeploymentManager.Models;

namespace GenericWeb.DeploymentManager.Services;

public enum DiagnosticExportSection
{
    All,
    CurrentLog,
    DeploymentHistory,
    CrashReports,
    ErrorReports,
    BuildPerformance,
    InternalDiagnostics,
    RuntimeResources
}

public sealed class DiagnosticBundleService
{
    public static string GetSuggestedFileName(DiagnosticExportSection section)
    {
        var suffix = section == DiagnosticExportSection.All ? "Diagnostic-Bundle" : "Diagnostic-" + section;
        return $"GWD-{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    public string Export(string destinationFolder, DeploymentProfile profile, DeploymentHistoryService history, string currentLog)
        => ExportSection(destinationFolder, DiagnosticExportSection.All, profile, history, currentLog, null, null);

    public string ExportSection(string destinationFolder, DiagnosticExportSection section, DeploymentProfile profile, DeploymentHistoryService history, string currentLog, BuildPerformanceTrace? performance, RuntimeMetricsService? runtimeMetrics)
    {
        Directory.CreateDirectory(destinationFolder);
        return ExportSectionToFile(Path.Combine(destinationFolder, GetSuggestedFileName(section)), section, profile, history, currentLog, performance, runtimeMetrics);
    }

    public string ExportSectionToFile(string destinationFile, DiagnosticExportSection section, DeploymentProfile profile, DeploymentHistoryService history, string currentLog, BuildPerformanceTrace? performance, RuntimeMetricsService? runtimeMetrics)
    {
        if (string.IsNullOrWhiteSpace(destinationFile)) throw new ArgumentException("A destination file is required.", nameof(destinationFile));
        if (!destinationFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) destinationFile += ".zip";
        var destinationFolder = Path.GetDirectoryName(destinationFile);
        if (string.IsNullOrWhiteSpace(destinationFolder)) throw new InvalidOperationException("The diagnostic destination folder could not be resolved.");
        Directory.CreateDirectory(destinationFolder);
        if (File.Exists(destinationFile)) File.Delete(destinationFile);

        using var archive = ZipFile.Open(destinationFile, ZipArchiveMode.Create);
        var appData = new AppDataService();

        if (section is DiagnosticExportSection.All)
        {
            AddText(archive, "runtime.json", JsonSerializer.Serialize(new
            {
                Product = "Generic Web Deployment Manager",
                Version = GenericWeb.DeploymentManager.ProductInfo.Version,
                ExportedAt = DateTimeOffset.Now,
                OS = Environment.OSVersion.VersionString,
                Runtime = Environment.Version.ToString(),
                Machine = Environment.MachineName,
                ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                profile.BuildMode,
                profile.DependencyResolutionMode,
                profile.OfflineBuild,
                profile.TargetMode,
                profile.WorkspaceRoot,
                profile.TargetFrameworkMajor,
                profile.RequiresNode
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.DeploymentHistory)
            AddDirectoryTextFiles(archive, history.Root, "history", "*.jsonl");

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.CurrentLog)
            if (File.Exists(currentLog)) AddSanitizedFile(archive, currentLog, "logs/current-session.log");

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.CrashReports)
            AddDirectoryTextFiles(archive, appData.CrashReports, "crash-reports", "*.txt");

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.ErrorReports)
            AddDirectoryTextFiles(archive, appData.ErrorReports, "error-reports", "*.txt");

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.InternalDiagnostics)
            AddDirectoryTextFiles(archive, appData.InternalDiagnosticsFolder, "internal-diagnostics", "*.log");

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.BuildPerformance)
        {
            if (performance is not null)
                AddText(archive, "performance/latest-build-performance.json", JsonSerializer.Serialize(performance, new JsonSerializerOptions { WriteIndented = true }));
            else
                AddText(archive, "performance/README.txt", "No in-memory build performance snapshot is available in this session.");
        }

        if (section is DiagnosticExportSection.All or DiagnosticExportSection.RuntimeResources)
        {
            if (runtimeMetrics is not null)
            {
                AddText(archive, "runtime-resources/history.json", runtimeMetrics.ToJson());
                AddText(archive, "runtime-resources/history.txt", runtimeMetrics.ToPlainText());
            }
            else AddText(archive, "runtime-resources/README.txt", "No in-memory CPU/memory history is available in this session.");
        }

        if (section is DiagnosticExportSection.All)
        {
            var repository = string.IsNullOrWhiteSpace(profile.DeploymentRepositoryFolder) ? profile.InstallerCacheFolder : profile.DeploymentRepositoryFolder;
            var buildState = Path.Combine(repository, "BuildState");
            AddDirectoryTextFiles(archive, buildState, "fingerprints", "*.json");
            if (Directory.Exists(profile.WorkspaceRoot))
                AddDirectoryTextFiles(archive, profile.WorkspaceRoot, "workspace", ".gwd-workspace-manifest.json");
            AddText(archive, "dependency-state.txt", BuildDependencyState(repository));
        }

        AddText(archive, "README.txt", $"Diagnostic export generated by {GenericWeb.DeploymentManager.ProductInfo.DisplayVersion}. Scope: {section}. Passwords, tokens, connection strings, and credentials are redacted.");
        return destinationFile;
    }

    private static string BuildDependencyState(string repository)
    {
        var paths = new[]
        {
            ("Repository", repository),
            ("NuGet packages", Path.Combine(repository, "NuGet", "packages")),
            ("NuGet feed", Path.Combine(repository, "NuGet", "feed")),
            ("npm cache", Path.Combine(repository, "Node", "npm-cache")),
            ("Build state", Path.Combine(repository, "BuildState"))
        };
        return string.Join(Environment.NewLine, paths.Select(x => $"{x.Item1}: {(Directory.Exists(x.Item2) ? "PRESENT" : "MISSING")} | {x.Item2}"));
    }

    private static void AddDirectoryTextFiles(ZipArchive archive, string root, string targetRoot, string pattern)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            AddSanitizedFile(archive, file, targetRoot + "/" + relative);
        }
    }

    private static void AddSanitizedFile(ZipArchive archive, string source, string name)
    {
        try { AddText(archive, name, Redact(File.ReadAllText(source))); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Diagnostic export file: " + source); AddText(archive, name + ".unavailable.txt", ex.Message); }
    }

    private static void AddText(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(SecretRedactor.Redact(text));
    }

    private static string Redact(string text) => SecretRedactor.Redact(text);
}
