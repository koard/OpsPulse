namespace Platform.Domain;

public static class HealthPolicy
{
    public static HealthReport Calculate(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var reasons = new List<string>();
        var score = 100;

        foreach (var process in snapshot.Processes.Where(process => !IsOnline(process)))
        {
            reasons.Add($"Process {process.Name} is {process.Status}.");
            score -= 35;
        }

        foreach (var endpoint in snapshot.Endpoints.Where(endpoint => endpoint.StatusCode is 0 or >= 500))
        {
            reasons.Add($"Endpoint {endpoint.Name} returned {endpoint.StatusCode}.");
            score -= 30;
        }

        if (snapshot.Metrics.CpuPercent >= 75)
        {
            reasons.Add($"CPU usage is {snapshot.Metrics.CpuPercent:0}%.");
            score -= 12;
        }

        if (snapshot.Metrics.MemoryPercent >= 75)
        {
            reasons.Add($"Memory usage is {snapshot.Metrics.MemoryPercent:0}%.");
            score -= 10;
        }

        if (snapshot.Metrics.DiskPercent >= 80)
        {
            reasons.Add($"Disk usage is {snapshot.Metrics.DiskPercent:0}%.");
            score -= 10;
        }

        var slowEndpoint = snapshot.Endpoints.FirstOrDefault(endpoint => endpoint.LatencyMs >= 800);
        if (slowEndpoint is not null)
        {
            reasons.Add($"{slowEndpoint.Name} latency is {slowEndpoint.LatencyMs} ms.");
            score -= 10;
        }

        var boundedScore = Math.Clamp(score, 0, 100);
        var status = reasons.Any(reason => reason.Contains("Process", StringComparison.OrdinalIgnoreCase) ||
                                           reason.Contains("returned", StringComparison.OrdinalIgnoreCase))
            ? HealthStatus.Critical
            : boundedScore switch
            {
                >= 85 => HealthStatus.Healthy,
                >= 55 => HealthStatus.Warning,
                _ => HealthStatus.Critical
            };

        if (reasons.Count == 0)
        {
            reasons.Add("All monitored signals are within policy.");
        }

        return new HealthReport(status, boundedScore, reasons);
    }

    private static bool IsOnline(ProcessMetric process)
    {
        return string.Equals(process.Status, "online", StringComparison.OrdinalIgnoreCase);
    }
}
