namespace Platform.Domain;

public static class AlertEvaluator
{
    public static IEnumerable<AlertEvent> Evaluate(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var process in snapshot.Processes.Where(process => !IsOnline(process)))
        {
            yield return new AlertEvent(
                Id: $"process-{snapshot.ProjectId}-{process.Name}",
                Severity: AlertSeverity.Critical,
                Title: $"{process.Name} process is down",
                Message: $"PM2 reports {process.Name} as {process.Status}.",
                CreatedAt: snapshot.CapturedAt);
        }

        foreach (var endpoint in snapshot.Endpoints.Where(endpoint => endpoint.StatusCode is 0 or >= 500))
        {
            yield return new AlertEvent(
                Id: $"endpoint-{snapshot.ProjectId}-{endpoint.Name}",
                Severity: AlertSeverity.Critical,
                Title: $"{endpoint.Name} health check failed",
                Message: $"{endpoint.Url} returned HTTP {endpoint.StatusCode}.",
                CreatedAt: endpoint.CheckedAt);
        }

        if (snapshot.Metrics.MemoryPercent >= 75)
        {
            yield return new AlertEvent(
                Id: $"memory-{snapshot.ProjectId}",
                Severity: AlertSeverity.Warning,
                Title: "memory pressure above baseline",
                Message: $"Host memory usage is {snapshot.Metrics.MemoryPercent:0}%.",
                CreatedAt: snapshot.CapturedAt);
        }

        if (snapshot.Metrics.CpuPercent >= 85)
        {
            yield return new AlertEvent(
                Id: $"cpu-{snapshot.ProjectId}",
                Severity: AlertSeverity.Warning,
                Title: "cpu pressure above baseline",
                Message: $"Host CPU usage is {snapshot.Metrics.CpuPercent:0}%.",
                CreatedAt: snapshot.CapturedAt);
        }
    }

    private static bool IsOnline(ProcessMetric process)
    {
        return string.Equals(process.Status, "online", StringComparison.OrdinalIgnoreCase);
    }
}
