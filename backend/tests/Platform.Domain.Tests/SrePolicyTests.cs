using Platform.Domain;

namespace Platform.Domain.Tests;

public sealed class SrePolicyTests
{
    [Fact]
    public void IncidentPolicy_GroupsDuplicateAlerts_ByFingerprint()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "errored");

        var firstPass = IncidentPolicy.ApplyAlerts([], snapshot, AlertEvaluator.Evaluate(snapshot));
        var secondPass = IncidentPolicy.ApplyAlerts(firstPass, snapshot, AlertEvaluator.Evaluate(snapshot));

        var backendIncident = Assert.Single(secondPass, incident => incident.Fingerprint.Contains("backend"));
        Assert.Equal(IncidentStatus.Open, backendIncident.Status);
        Assert.Equal(2, backendIncident.Occurrences);
    }

    [Fact]
    public void IncidentPolicy_ReopensResolvedIncident_WhenFailureReturns()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "stopped");
        var incident = IncidentPolicy.ApplyAlerts([], snapshot, AlertEvaluator.Evaluate(snapshot)).Single();
        var resolved = incident with
        {
            Status = IncidentStatus.Resolved,
            AcknowledgedAt = snapshot.CapturedAt.AddSeconds(30),
            ResolvedAt = snapshot.CapturedAt.AddMinutes(1)
        };

        var reopened = IncidentPolicy.ApplyAlerts([resolved], snapshot, AlertEvaluator.Evaluate(snapshot)).Single();

        Assert.Equal(IncidentStatus.Open, reopened.Status);
        Assert.Null(reopened.AcknowledgedAt);
        Assert.Null(reopened.ResolvedAt);
        Assert.Equal(2, reopened.Occurrences);
    }

    [Fact]
    public void IncidentPolicy_KeepsAcknowledgedIncidentGrouped_WhenAlertPersists()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "stopped");
        var incident = IncidentPolicy.ApplyAlerts([], snapshot, AlertEvaluator.Evaluate(snapshot)).Single();
        var acknowledged = incident with
        {
            Status = IncidentStatus.Acknowledged,
            AcknowledgedAt = snapshot.CapturedAt.AddSeconds(30)
        };

        var grouped = IncidentPolicy.ApplyAlerts([acknowledged], snapshot, AlertEvaluator.Evaluate(snapshot)).Single();

        Assert.Equal(IncidentStatus.Acknowledged, grouped.Status);
        Assert.Equal(acknowledged.AcknowledgedAt, grouped.AcknowledgedAt);
        Assert.Equal(2, grouped.Occurrences);
    }

    [Fact]
    public void AgentPolicy_FlagsStaleAgent_AndMissingExpectedProcesses()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "online") with
        {
            Agent = new AgentMetadata(
                Version: "1.0.0",
                Hostname: "duke-prod-01",
                Os: "Ubuntu 22.04",
                Pm2Version: "5.4.2",
                ReceivedAt: DateTimeOffset.Parse("2026-05-10T10:00:00Z")),
            Processes =
            [
                new ProcessMetric("dukefarm-backend", "Express API", "online", 8, 180, 0, 900)
            ]
        };

        var status = AgentPolicy.Evaluate(
            snapshot,
            expectedProcessNames: ["dukefarm-backend", "dukefarm-admin"],
            now: DateTimeOffset.Parse("2026-05-10T10:03:01Z"),
            staleAfter: TimeSpan.FromMinutes(3));

        Assert.True(status.IsStale);
        Assert.Contains(status.DriftIssues, issue => issue.Kind == DriftIssueKind.MissingExpectedProcess);
    }

    [Fact]
    public void RootCauseAnalyzer_ConnectsDownProcessAndFailingEndpoint()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "errored") with
        {
            Endpoints =
            [
                new EndpointCheck(
                    Name: "DukeFarm API",
                    Url: "http://127.0.0.1:4000/healthz",
                    StatusCode: 503,
                    LatencyMs: 210,
                    CheckedAt: DateTimeOffset.Parse("2026-05-10T10:00:00Z"))
            ]
        };

        var hints = RootCauseAnalyzer.Analyze(snapshot);

        Assert.Contains(hints, hint => hint.Code == "process_endpoint_failure");
    }

    [Fact]
    public void SloCalculator_ComputesAvailabilityAndLatencyPercentiles()
    {
        var history = new[]
        {
            SnapshotFactory.Create(endpointStatusCode: 200, endpointLatencyMs: 100),
            SnapshotFactory.Create(endpointStatusCode: 200, endpointLatencyMs: 200),
            SnapshotFactory.Create(endpointStatusCode: 500, endpointLatencyMs: 900),
            SnapshotFactory.Create(endpointStatusCode: 200, endpointLatencyMs: 300)
        };

        var report = SloCalculator.Calculate("student-portal", history, targetAvailabilityPercent: 99.9);
        var endpoint = Assert.Single(report.Endpoints);

        Assert.Equal(75, endpoint.AvailabilityPercent);
        Assert.Equal(900, endpoint.P99LatencyMs);
        Assert.True(report.ErrorBudgetBurnedPercent > 0);
    }
}
