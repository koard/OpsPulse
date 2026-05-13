namespace Platform.Domain;

public static class AgentPolicy
{
    public static AgentStatus Evaluate(
        ProjectSnapshot snapshot,
        IReadOnlyList<string> expectedProcessNames,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expectedProcessNames);

        var issues = new List<DriftIssue>();
        var lastSeen = snapshot.Agent?.ReceivedAt ?? snapshot.CapturedAt;
        var isStale = now - lastSeen > staleAfter;
        var observedProcesses = snapshot.Processes
            .Select(process => process.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedProcesses = expectedProcessNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in expectedProcesses.Where(expected => !observedProcesses.Contains(expected)))
        {
            issues.Add(new DriftIssue(
                DriftIssueKind.MissingExpectedProcess,
                $"Expected process '{expected}' was not reported by PM2."));
        }

        foreach (var observed in observedProcesses.Where(observed => !expectedProcesses.Contains(observed)))
        {
            issues.Add(new DriftIssue(
                DriftIssueKind.UnknownProcess,
                $"Unexpected process '{observed}' is running."));
        }

        if (isStale)
        {
            issues.Add(new DriftIssue(
                DriftIssueKind.StaleAgent,
                $"Agent last reported at {lastSeen:u}."));
        }

        return new AgentStatus(snapshot.ProjectId, snapshot.Agent, isStale, lastSeen, issues);
    }
}
