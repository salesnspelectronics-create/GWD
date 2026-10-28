namespace GenericWeb.DeploymentManager.Models;

public sealed class DeploymentHistoryRecord
{
    public int SchemaVersion { get; set; } = 2;
    public string EventType { get; set; } = "Started";
    public string AttemptId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string DeploymentTargetPath { get; set; } = string.Empty;
    public string ApplicationVersion { get; set; } = string.Empty;
    public string BuildMode { get; set; } = string.Empty;
    public bool OfflineBuild { get; set; }
    public string Result { get; set; } = "InProgress";
    public string SessionLog { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double ElapsedSeconds { get; set; }
    public double? IntervalSincePreviousSeconds { get; set; }
    public DateTime EventAtUtc { get; set; }
    public BuildPerformanceHistory? BuildPerformance { get; set; }
}

public sealed class BuildPerformanceHistory
{
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string BuildMode { get; set; } = string.Empty;
    public string DependencyMode { get; set; } = string.Empty;
    public double TotalMilliseconds { get; set; }
    public List<BuildPerformanceStageHistory> Stages { get; set; } = new();
}

public sealed class BuildPerformanceStageHistory
{
    public string Name { get; set; } = string.Empty;
    public double ElapsedMilliseconds { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class DeploymentProjectSummary
{
    public string ProjectId { get; init; } = string.Empty;
    public string ApplicationName { get; init; } = string.Empty;
    public string SiteName { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public int TotalAttempts { get; init; }
    public int SuccessfulAttempts { get; init; }
    public int FailedAttempts { get; init; }
    public int CancelledAttempts { get; init; }
    public int IncompleteAttempts { get; init; }
    public int DistinctVersions { get; init; }
    public DateTime? FirstDeploymentUtc { get; init; }
    public DateTime? LatestDeploymentUtc { get; init; }
    public TimeSpan CalendarLifetime { get; init; }
    public TimeSpan CumulativeRuntime { get; init; }
    public TimeSpan AverageRuntime { get; init; }
    public TimeSpan AverageSuccessfulRuntime { get; init; }
    public TimeSpan FastestRuntime { get; init; }
    public TimeSpan SlowestRuntime { get; init; }
    public TimeSpan AverageInterval { get; init; }
    public int SmartDeployCount { get; init; }
    public int FullDeployCount { get; init; }
    public int PreparedDeployCount { get; init; }
    public IReadOnlyList<DeploymentHistoryRecord> Attempts { get; init; } = Array.Empty<DeploymentHistoryRecord>();
}
