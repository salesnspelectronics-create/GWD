using GenericWeb.DeploymentManager.CodeQuality;
using GenericWeb.DeploymentManager.Models;
using System.Text;

namespace GenericWeb.DeploymentManager.Services;

// All entry points are fail-open. Collection failure must never change process results.
public sealed class CodeQualitySession
{
    private DiagnosticCollector? _collector;
    public static DiagnosticStore Store(AppDataService appData) => new(Path.Combine(appData.Root, "CodeQuality"));
    public void Start(AppDataService appData, DeploymentProfile profile, string projectId, string attemptId)
    {
        try
        {
            var root = profile.SourceFolder;
            var fingerprint = "Unavailable";
            var status = "Collecting";
            try { fingerprint = Fingerprint(root); }
            catch (Exception ex) { status = "Partial"; InternalDiagnostics.Record(ex, "Code Quality source fingerprint (non-blocking)"); }
            _collector = new DiagnosticCollector(new BuildDiagnostics
            {
                ProjectId = projectId, AttemptId = attemptId, BuildMode = profile.BuildMode,
                SourceFingerprint = fingerprint, CollectionStatus = status,
                EnvironmentFingerprint = DiagnosticParser.Hash($"{Environment.OSVersion}|{Environment.Version}|{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}|{profile.OfflineBuild}|{profile.DependencyResolutionMode}")
            }, Store(appData), SecretRedactor.Redact);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Code Quality start (non-blocking)"); }
    }
    private static string Fingerprint(string root)
    {
        if (!Directory.Exists(root)) return "Unavailable";
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var file in EnumerateSource(root).OrderBy(x => x, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file).Replace('\\', '/') + "\n"));
            using var stream = File.OpenRead(file);
            var buffer = new byte[65536];
            int read;
            while ((read = stream.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static IEnumerable<string> EnumerateSource(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root)) yield return file;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (new[] { ".git", "node_modules", "bin", "obj", ".worktrees", "dist", "publish" }.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)) continue;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var file in EnumerateSource(folder)) yield return file;
        }
    }
    public Stage? Begin(string tool, string arguments)
    {
        try { return _collector?.Begin(tool, arguments); }
        catch (Exception ex) { Failed(ex); return null; }
    }
    public void Line(Stage? stage, string stream, string line)
    {
        if (stage is null) return;
        try { _collector?.Line(stage, stream, line); } catch (Exception ex) { Failed(ex); }
    }
    public void End(Stage? stage, int? code, string status)
    {
        if (stage is null) return;
        try { _collector?.End(stage, code, status); } catch (Exception ex) { Failed(ex); }
    }
    public void Finish(string result)
    {
        try { _collector?.Finish(result); } catch (Exception ex) { Failed(ex); }
        finally { _collector = null; }
    }
    private void Failed(Exception ex)
    {
        if (_collector is not null) _collector.Build.CollectionStatus = "Partial";
        InternalDiagnostics.Record(ex, "Code Quality collection (non-blocking)");
    }
}
