namespace Platform.Domain;

public static class SloCalculator
{
    public static SloReport Calculate(
        string projectId,
        IReadOnlyList<ProjectSnapshot> history,
        double targetAvailabilityPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(history);

        var endpointChecks = history
            .SelectMany(snapshot => snapshot.Endpoints)
            .GroupBy(endpoint => $"{endpoint.Name}|{endpoint.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var checks = group.ToList();
                var failedChecks = checks.Count(check => check.StatusCode is 0 or >= 500);
                var latencies = checks.Select(check => check.LatencyMs).Order().ToList();
                var sample = checks[0];

                return new EndpointSlo(
                    Name: sample.Name,
                    Url: sample.Url,
                    AvailabilityPercent: Percent(checks.Count - failedChecks, checks.Count),
                    P95LatencyMs: Percentile(latencies, 0.95),
                    P99LatencyMs: Percentile(latencies, 0.99),
                    TotalChecks: checks.Count,
                    FailedChecks: failedChecks);
            })
            .ToList();

        var totalChecks = endpointChecks.Sum(endpoint => endpoint.TotalChecks);
        var totalFailures = endpointChecks.Sum(endpoint => endpoint.FailedChecks);
        var availability = totalChecks == 0 ? 100 : Percent(totalChecks - totalFailures, totalChecks);
        var allowedFailurePercent = Math.Max(100 - targetAvailabilityPercent, 0.0001);
        var actualFailurePercent = 100 - availability;

        return new SloReport(
            ProjectId: projectId,
            TargetAvailabilityPercent: targetAvailabilityPercent,
            AvailabilityPercent: availability,
            ErrorBudgetBurnedPercent: Math.Round(actualFailurePercent / allowedFailurePercent * 100, 2),
            Endpoints: endpointChecks);
    }

    private static double Percent(int numerator, int denominator)
    {
        return denominator == 0
            ? 100
            : Math.Round((double)numerator / denominator * 100, 2);
    }

    private static int Percentile(IReadOnlyList<int> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Count - 1)];
    }
}
