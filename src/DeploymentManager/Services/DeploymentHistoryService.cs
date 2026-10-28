using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GenericWeb.DeploymentManager.Models;

namespace GenericWeb.DeploymentManager.Services;

public sealed class DeploymentHistoryService
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public string Root { get; } = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GWDM_APPDATA_ROOT")) ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GenericWebDeploymentManager",
        "History") : Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("GWDM_APPDATA_ROOT")!), "History");

    public DeploymentHistoryService()
    {
        Directory.CreateDirectory(Root);
    }

    public string GetProjectId(DeploymentProfile profile)
    {
        var application = NormalizeIdentity(profile.ApplicationName);
        var site = NormalizeIdentity(profile.SiteName);
        var database = NormalizeIdentity(profile.DatabaseName);

        if (string.IsNullOrWhiteSpace(application) && string.IsNullOrWhiteSpace(site) && string.IsNullOrWhiteSpace(database))
        {
            application = NormalizeIdentity(new DirectoryInfo(profile.SourceFolder).Name);
        }

        var canonical = $"app={application}|site={site}|db={database}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    public DeploymentHistoryRecord BeginAttempt(DeploymentProfile profile, string sessionLog)
    {
        var started = DateTime.UtcNow;
        var projectId = GetProjectId(profile);
        var previous = GetAttempts(projectId)
            .Where(x => x.EndedAtUtc.HasValue)
            .OrderByDescending(x => x.EndedAtUtc)
            .FirstOrDefault();

        var record = new DeploymentHistoryRecord
        {
            EventType = "Started",
            AttemptId = Guid.NewGuid().ToString("N"),
            ProjectId = projectId,
            ApplicationName = profile.ApplicationName.Trim(),
            SiteName = profile.SiteName.Trim(),
            DatabaseName = profile.DatabaseName.Trim(),
            SourceFolder = profile.SourceFolder,
            DeploymentTargetPath = profile.HostingRoot,
            ApplicationVersion = DetectApplicationVersion(profile),
            BuildMode = profile.BuildMode,
            OfflineBuild = profile.OfflineBuild,
            Result = "InProgress",
            SessionLog = sessionLog,
            StartedAtUtc = started,
            IntervalSincePreviousSeconds = previous?.EndedAtUtc is DateTime previousEnd
                ? Math.Max(0, (started - previousEnd).TotalSeconds)
                : null,
            EventAtUtc = started
        };

        Append(record);
        return record;
    }

    public void CompleteAttempt(DeploymentHistoryRecord started, DeploymentProfile profile, string result, TimeSpan elapsed, string? deploymentTargetPath, BuildPerformanceTrace? performance)
    {
        var completed = new DeploymentHistoryRecord
        {
            SchemaVersion = started.SchemaVersion,
            EventType = "Completed",
            AttemptId = started.AttemptId,
            ProjectId = started.ProjectId,
            ApplicationName = profile.ApplicationName.Trim(),
            SiteName = profile.SiteName.Trim(),
            DatabaseName = profile.DatabaseName.Trim(),
            SourceFolder = profile.SourceFolder,
            DeploymentTargetPath = string.IsNullOrWhiteSpace(deploymentTargetPath) ? profile.HostingRoot : deploymentTargetPath,
            ApplicationVersion = string.IsNullOrWhiteSpace(started.ApplicationVersion) ? DetectApplicationVersion(profile) : started.ApplicationVersion,
            BuildMode = profile.BuildMode,
            OfflineBuild = profile.OfflineBuild,
            Result = NormalizeResult(result),
            SessionLog = started.SessionLog,
            StartedAtUtc = started.StartedAtUtc,
            EndedAtUtc = DateTime.UtcNow,
            ElapsedSeconds = Math.Max(0, elapsed.TotalSeconds),
            IntervalSincePreviousSeconds = started.IntervalSincePreviousSeconds,
            EventAtUtc = DateTime.UtcNow
        };

        if (performance is not null)
        {
            completed.BuildPerformance = new BuildPerformanceHistory
            {
                StartedAt = performance.StartedAt,
                CompletedAt = performance.CompletedAt,
                BuildMode = performance.BuildMode,
                DependencyMode = performance.DependencyMode,
                TotalMilliseconds = performance.TotalElapsed.TotalMilliseconds,
                Stages = performance.Stages.Select(x => new BuildPerformanceStageHistory
                {
                    Name = x.Name,
                    ElapsedMilliseconds = x.Elapsed.TotalMilliseconds,
                    Status = x.Status,
                    Detail = x.Detail
                }).ToList()
            };
        }

        Append(completed);
    }

    public IReadOnlyList<DeploymentHistoryRecord> GetAttempts(string projectId)
    {
        var path = GetProjectFile(projectId);
        if (!File.Exists(path)) return Array.Empty<DeploymentHistoryRecord>();

        var events = new List<DeploymentHistoryRecord>();
        lock (_sync)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<DeploymentHistoryRecord>(line);
                    if (item is not null && !string.IsNullOrWhiteSpace(item.AttemptId)) events.Add(item);
                }
                catch (Exception ex)
                {
                    InternalDiagnostics.Record(ex, "Read deployment history JSONL record");
                    // Keep history readable even if a single append was interrupted or corrupted.
                }
            }
        }

        return events
            .GroupBy(x => x.AttemptId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.EventAtUtc).First())
            .OrderBy(x => x.StartedAtUtc)
            .ToArray();
    }

    public IReadOnlyList<DeploymentProjectSummary> GetProjectSummaries()
    {
        Directory.CreateDirectory(Root);
        var summaries = new List<DeploymentProjectSummary>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            var projectId = Path.GetFileNameWithoutExtension(path);
            var attempts = GetAttempts(projectId);
            if (attempts.Count == 0) continue;
            summaries.Add(BuildSummary(projectId, attempts));
        }

        return summaries
            .OrderByDescending(x => x.LatestDeploymentUtc ?? DateTime.MinValue)
            .ThenBy(x => x.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DeploymentProjectSummary? GetSummary(string projectId)
    {
        var attempts = GetAttempts(projectId);
        return attempts.Count == 0 ? null : BuildSummary(projectId, attempts);
    }

    private DeploymentProjectSummary BuildSummary(string projectId, IReadOnlyList<DeploymentHistoryRecord> attempts)
    {
        var ordered = attempts.OrderBy(x => x.StartedAtUtc).ToArray();
        var completed = ordered.Where(x => x.EndedAtUtc.HasValue).ToArray();
        var durations = completed.Select(x => TimeSpan.FromSeconds(Math.Max(0, x.ElapsedSeconds))).ToArray();
        var successfulDurations = completed.Where(x => IsResult(x, "Successful")).Select(x => TimeSpan.FromSeconds(Math.Max(0, x.ElapsedSeconds))).ToArray();
        var intervals = completed
            .Where(x => x.IntervalSincePreviousSeconds.HasValue)
            .Select(x => TimeSpan.FromSeconds(Math.Max(0, x.IntervalSincePreviousSeconds!.Value)))
            .ToArray();
        var successful = completed.Where(x => IsResult(x, "Successful")).ToArray();
        var first = ordered.FirstOrDefault();
        var latest = ordered.LastOrDefault();

        return new DeploymentProjectSummary
        {
            ProjectId = projectId,
            ApplicationName = latest?.ApplicationName ?? first?.ApplicationName ?? "Unknown application",
            SiteName = latest?.SiteName ?? first?.SiteName ?? string.Empty,
            DatabaseName = latest?.DatabaseName ?? first?.DatabaseName ?? string.Empty,
            TotalAttempts = ordered.Length,
            SuccessfulAttempts = completed.Count(x => IsResult(x, "Successful")),
            FailedAttempts = completed.Count(x => IsResult(x, "Failed") || IsResult(x, "RecoveryFailed")),
            CancelledAttempts = completed.Count(x => IsResult(x, "Cancelled")),
            IncompleteAttempts = ordered.Count(x => !x.EndedAtUtc.HasValue),
            DistinctVersions = successful
                .Select(x => x.ApplicationVersion)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            FirstDeploymentUtc = first?.StartedAtUtc,
            LatestDeploymentUtc = latest?.EndedAtUtc ?? latest?.StartedAtUtc,
            CalendarLifetime = first is null || latest is null
                ? TimeSpan.Zero
                : (latest.EndedAtUtc ?? latest.StartedAtUtc) - first.StartedAtUtc,
            CumulativeRuntime = Sum(durations),
            AverageRuntime = Average(durations),
            AverageSuccessfulRuntime = Average(successfulDurations),
            FastestRuntime = durations.Length == 0 ? TimeSpan.Zero : durations.Min(),
            SlowestRuntime = durations.Length == 0 ? TimeSpan.Zero : durations.Max(),
            AverageInterval = Average(intervals),
            SmartDeployCount = completed.Count(x => string.Equals(x.BuildMode, "Fast Local Test", StringComparison.OrdinalIgnoreCase) || string.Equals(x.BuildMode, "Fast IIS Update", StringComparison.OrdinalIgnoreCase) || string.Equals(x.BuildMode, "Fast Build", StringComparison.OrdinalIgnoreCase) || string.Equals(x.BuildMode, "Smart Deploy", StringComparison.OrdinalIgnoreCase)),
            FullDeployCount = completed.Count(x => string.Equals(x.BuildMode, "Full IIS Deploy", StringComparison.OrdinalIgnoreCase) || string.Equals(x.BuildMode, "Full Deploy", StringComparison.OrdinalIgnoreCase)),
            PreparedDeployCount = completed.Count(x => x.BuildMode.Contains("prepared", StringComparison.OrdinalIgnoreCase) || x.BuildMode.Contains("publish folder", StringComparison.OrdinalIgnoreCase)),
            Attempts = ordered
        };
    }

    private void Append(DeploymentHistoryRecord record)
    {
        Directory.CreateDirectory(Root);
        var path = GetProjectFile(record.ProjectId);
        var json = SecretRedactor.Redact(JsonSerializer.Serialize(record, _json)) + Environment.NewLine;
        lock (_sync)
        {
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    private string GetProjectFile(string projectId) => Path.Combine(Root, projectId + ".jsonl");

    private static string NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static string NormalizeResult(string result)
    {
        if (result.Equals("Successful", StringComparison.OrdinalIgnoreCase)) return "Successful";
        if (result.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
        if (result.Equals("RecoveryFailed", StringComparison.OrdinalIgnoreCase)) return "RecoveryFailed";
        return "Failed";
    }

    private static bool IsResult(DeploymentHistoryRecord record, string result) =>
        string.Equals(record.Result, result, StringComparison.OrdinalIgnoreCase);

    private static TimeSpan Sum(IEnumerable<TimeSpan> values) => TimeSpan.FromTicks(values.Sum(x => x.Ticks));
    private static TimeSpan Average(IReadOnlyCollection<TimeSpan> values) => values.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)values.Average(x => x.Ticks));

    private static string DetectApplicationVersion(DeploymentProfile profile)
    {
        try
        {
            var versionPath = profile.BuildMode == "Prepared IIS Publish" && !string.IsNullOrWhiteSpace(profile.ReadyPublishFolder)
                ? profile.ReadyPublishFolder
                : profile.SourceFolder;
            var sourceName = new DirectoryInfo(versionPath).Name;
            var match = Regex.Match(sourceName, @"(?i)(?:^|[-_\s])v?(\d+\.\d+(?:\.\d+){0,2})(?:$|[-_\s])");
            if (match.Success) return match.Groups[1].Value;
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Detect application version from source folder"); }

        try
        {
            if (File.Exists(profile.ApiProjectPath))
            {
                var document = XDocument.Load(profile.ApiProjectPath);
                var candidates = new[] { "Version", "FileVersion", "AssemblyVersion" };
                foreach (var name in candidates)
                {
                    var value = document.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Detect application version from project file"); }

        return string.Empty;
    }
}
