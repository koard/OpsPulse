namespace Platform.Domain;

public static class RootCauseAnalyzer
{
    public static IReadOnlyList<RootCauseHint> Analyze(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var hints = new List<RootCauseHint>();
        var hasDownProcess = snapshot.Processes.Any(process =>
            !string.Equals(process.Status, "online", StringComparison.OrdinalIgnoreCase));
        var hasFailingEndpoint = snapshot.Endpoints.Any(endpoint =>
            endpoint.StatusCode is 0 or >= 500);
        var hasHighRestartCount = snapshot.Processes.Any(process => process.Restarts >= 3);
        var hasSlowEndpoint = snapshot.Endpoints.Any(endpoint => endpoint.LatencyMs >= 800);

        if (hasDownProcess && hasFailingEndpoint)
        {
            hints.Add(new RootCauseHint(
                Code: "process_endpoint_failure",
                Title: "Backend process likely down",
                Detail: "A PM2 process is not online while an endpoint is returning errors.",
                Severity: AlertSeverity.Critical));
        }

        if (snapshot.Metrics.MemoryPercent >= 75 && hasHighRestartCount)
        {
            hints.Add(new RootCauseHint(
                Code: "memory_pressure_restart_loop",
                Title: "Possible memory pressure restart loop",
                Detail: "Memory is above baseline and process restart counts are increasing.",
                Severity: AlertSeverity.Warning));
        }

        if (!hasDownProcess && hasSlowEndpoint)
        {
            hints.Add(new RootCauseHint(
                Code: "latency_without_process_failure",
                Title: "Possible database, network, or API slowness",
                Detail: "Processes are online, but endpoint latency is high.",
                Severity: AlertSeverity.Warning));
        }

        if (hints.Count == 0)
        {
            hints.Add(new RootCauseHint(
                Code: "no_obvious_root_cause",
                Title: "No obvious root cause",
                Detail: "Signals do not match a known incident pattern yet.",
                Severity: AlertSeverity.Info));
        }

        return hints;
    }
}
