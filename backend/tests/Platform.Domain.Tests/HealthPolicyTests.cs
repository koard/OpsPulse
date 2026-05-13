using Platform.Domain;

namespace Platform.Domain.Tests;

public sealed class HealthPolicyTests
{
    [Fact]
    public void Calculate_ReturnsCritical_WhenAnyProcessIsNotOnline()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "errored");

        var result = HealthPolicy.Calculate(snapshot);

        Assert.Equal(HealthStatus.Critical, result.Status);
        Assert.Contains("process", result.Reasons[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_ReturnsWarning_WhenResourcePressureIsHigh()
    {
        var snapshot = SnapshotFactory.Create(cpuPercent: 82, memoryPercent: 77);

        var result = HealthPolicy.Calculate(snapshot);

        Assert.Equal(HealthStatus.Warning, result.Status);
    }

    [Fact]
    public void AlertEvaluator_EmitsCriticalAlert_ForDownProcess()
    {
        var snapshot = SnapshotFactory.Create(processStatus: "stopped");

        var alerts = AlertEvaluator.Evaluate(snapshot).ToList();

        Assert.Contains(alerts, alert => alert.Severity == AlertSeverity.Critical);
    }
}

internal static class SnapshotFactory
{
    public static ProjectSnapshot Create(
        string processStatus = "online",
        double cpuPercent = 35,
        double memoryPercent = 45,
        int endpointStatusCode = 200,
        int endpointLatencyMs = 142)
    {
        return new ProjectSnapshot(
            ProjectId: "student-portal",
            CapturedAt: DateTimeOffset.Parse("2026-05-10T10:00:00Z"),
            Metrics: new HostMetrics(
                CpuPercent: cpuPercent,
                MemoryPercent: memoryPercent,
                DiskPercent: 51,
                NetworkInMbps: 12.1,
                NetworkOutMbps: 4.8),
            Processes:
            [
                new ProcessMetric(
                    Name: "backend",
                    Role: ".NET API",
                    Status: processStatus,
                    CpuPercent: 12,
                    MemoryMb: 220,
                    Restarts: 0,
                    UptimeSeconds: 4800)
            ],
            Endpoints:
            [
                new EndpointCheck(
                    Name: "Admin UI",
                    Url: "https://admin.example.ac.th",
                    StatusCode: endpointStatusCode,
                    LatencyMs: endpointLatencyMs,
                    CheckedAt: DateTimeOffset.Parse("2026-05-10T09:59:30Z"))
            ]);
    }
}
