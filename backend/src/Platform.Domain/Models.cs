namespace Platform.Domain;

public enum HealthStatus
{
    Healthy,
    Warning,
    Critical
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum IncidentStatus
{
    Open,
    Acknowledged,
    Resolved
}

public enum DriftIssueKind
{
    MissingExpectedProcess,
    UnknownProcess,
    StaleAgent
}

public enum OpsCommandAction
{
    HealthCheckNow,
    Pm2RestartProcess,
    RedeployBackend,
    RedeployFrontend,
    RedeployAdmin,
    PrismaMigrateDeploy,
    RollbackBackend,
    RollbackFrontend,
    RollbackAdmin
}

public enum OpsCommandStatus
{
    Pending,
    Claimed,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled
}

public sealed record ServerProfile(
    string Hostname,
    string Os,
    string Access,
    string ReverseProxy,
    string ProcessManager);

public sealed record MonitoredProject(
    string Id,
    string Name,
    string Environment,
    string Owner,
    ServerProfile Server,
    IReadOnlyList<EndpointTarget> Endpoints);

public sealed record EndpointTarget(
    string Name,
    string Url,
    string HealthPath);

public sealed record HostMetrics(
    double CpuPercent,
    double MemoryPercent,
    double DiskPercent,
    double NetworkInMbps,
    double NetworkOutMbps);

public sealed record ProcessMetric(
    string Name,
    string Role,
    string Status,
    double CpuPercent,
    int MemoryMb,
    int Restarts,
    long UptimeSeconds);

public sealed record EndpointCheck(
    string Name,
    string Url,
    int StatusCode,
    int LatencyMs,
    DateTimeOffset CheckedAt);

public sealed record AgentMetadata(
    string Version,
    string Hostname,
    string Os,
    string Pm2Version,
    DateTimeOffset ReceivedAt);

public sealed record ProjectSnapshot(
    string ProjectId,
    DateTimeOffset CapturedAt,
    HostMetrics Metrics,
    IReadOnlyList<ProcessMetric> Processes,
    IReadOnlyList<EndpointCheck> Endpoints,
    AgentMetadata? Agent = null);

public sealed record HealthReport(
    HealthStatus Status,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed record AlertEvent(
    string Id,
    AlertSeverity Severity,
    string Title,
    string Message,
    DateTimeOffset CreatedAt,
    bool Acknowledged = false);

public sealed record TimelinePoint(
    string At,
    int HealthScore,
    int LatencyMs);

public sealed record Incident(
    string Id,
    string ProjectId,
    string Fingerprint,
    AlertSeverity Severity,
    string Title,
    string Summary,
    IncidentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt,
    string RootCauseHint,
    int Occurrences,
    DateTimeOffset? AcknowledgedAt = null,
    DateTimeOffset? ResolvedAt = null);

public sealed record RootCauseHint(
    string Code,
    string Title,
    string Detail,
    AlertSeverity Severity);

public sealed record DriftIssue(
    DriftIssueKind Kind,
    string Message);

public sealed record AgentStatus(
    string ProjectId,
    AgentMetadata? Agent,
    bool IsStale,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<DriftIssue> DriftIssues);

public sealed record EndpointSlo(
    string Name,
    string Url,
    double AvailabilityPercent,
    int P95LatencyMs,
    int P99LatencyMs,
    int TotalChecks,
    int FailedChecks);

public sealed record SloReport(
    string ProjectId,
    double TargetAvailabilityPercent,
    double AvailabilityPercent,
    double ErrorBudgetBurnedPercent,
    IReadOnlyList<EndpointSlo> Endpoints);

public sealed record OpsCommand(
    string Id,
    string ProjectId,
    OpsCommandAction Action,
    string Target,
    OpsCommandStatus Status,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ClaimedAt = null,
    DateTimeOffset? FinishedAt = null,
    string? Summary = null,
    string? StdoutTail = null,
    string? StderrTail = null,
    string? ReleaseCommit = null);

public sealed record CommandAuditEntry(
    string Id,
    string CommandId,
    string ProjectId,
    string Event,
    DateTimeOffset CreatedAt,
    string Message);

public sealed record CommandDecision(
    bool IsAccepted,
    OpsCommand? Command,
    string? Error);
