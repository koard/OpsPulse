namespace Platform.Domain;

public static class DemoData
{
    public const string PrimaryProjectId = "dukefarm-production";

    public static IReadOnlyList<MonitoredProject> Projects { get; } =
    [
        new MonitoredProject(
            Id: PrimaryProjectId,
            Name: "DukeFarm Production",
            Environment: "Production",
            Owner: "DukeFarm Operations",
            Server: new ServerProfile(
                Hostname: "dukefarm-prod-01",
                Os: "Ubuntu 22.04 LTS",
                Access: "Public HTTPS ingest",
                ReverseProxy: "Nginx",
                ProcessManager: "PM2"),
            Endpoints:
            [
                new EndpointTarget("DukeFarm API", "http://127.0.0.1:4000", "/healthz"),
                new EndpointTarget("DukeFarm API v1", "http://127.0.0.1:4000", "/api/v1/health")
            ])
    ];

    public static ProjectSnapshot LatestSnapshot { get; } = new(
        ProjectId: PrimaryProjectId,
        CapturedAt: DateTimeOffset.Parse("2026-05-10T10:00:00Z"),
        Metrics: new HostMetrics(
            CpuPercent: 71,
            MemoryPercent: 78,
            DiskPercent: 68,
            NetworkInMbps: 12.4,
            NetworkOutMbps: 5.8),
        Processes:
        [
            new ProcessMetric(
                Name: "dukefarm-backend",
                Role: "Express API",
                Status: "errored",
                CpuPercent: 18,
                MemoryMb: 184,
                Restarts: 5,
                UptimeSeconds: 74_000),
            new ProcessMetric(
                Name: "dukefarm-admin",
                Role: "Admin app",
                Status: "online",
                CpuPercent: 9,
                MemoryMb: 132,
                Restarts: 0,
                UptimeSeconds: 65_000),
            new ProcessMetric(
                Name: "opspulse-agent",
                Role: "Telemetry sender",
                Status: "online",
                CpuPercent: 4,
                MemoryMb: 64,
                Restarts: 0,
                UptimeSeconds: 320)
        ],
        Endpoints:
        [
            new EndpointCheck(
                Name: "DukeFarm API",
                Url: "http://127.0.0.1:4000/healthz",
                StatusCode: 200,
                LatencyMs: 178,
                CheckedAt: DateTimeOffset.Parse("2026-05-10T09:59:30Z")),
            new EndpointCheck(
                Name: "DukeFarm API v1",
                Url: "http://127.0.0.1:4000/api/v1/health",
                StatusCode: 200,
                LatencyMs: 226,
                CheckedAt: DateTimeOffset.Parse("2026-05-10T09:59:30Z")),
            new EndpointCheck(
                Name: "DukeFarm API",
                Url: "http://127.0.0.1:4000/healthz",
                StatusCode: 503,
                LatencyMs: 940,
                CheckedAt: DateTimeOffset.Parse("2026-05-10T09:59:30Z"))
        ],
        Agent: new AgentMetadata(
            Version: "1.0.0",
            Hostname: "dukefarm-prod-01",
            Os: "Ubuntu 22.04 LTS",
            Pm2Version: "5.4.2",
            ReceivedAt: DateTimeOffset.Parse("2026-05-10T10:00:00Z")));

    public static IReadOnlyList<ProjectSnapshot> SnapshotHistory { get; } =
    [
        LatestSnapshot with
        {
            CapturedAt = DateTimeOffset.Parse("2026-05-10T09:42:00Z"),
            Metrics = LatestSnapshot.Metrics with { CpuPercent = 38, MemoryPercent = 52 },
            Endpoints = LatestSnapshot.Endpoints.Select(endpoint => endpoint with { StatusCode = 200, LatencyMs = 118 }).ToList(),
            Processes = LatestSnapshot.Processes.Select(process => process with { Status = "online" }).ToList()
        },
        LatestSnapshot with
        {
            CapturedAt = DateTimeOffset.Parse("2026-05-10T09:45:00Z"),
            Metrics = LatestSnapshot.Metrics with { CpuPercent = 42, MemoryPercent = 55 },
            Endpoints = LatestSnapshot.Endpoints.Select(endpoint => endpoint with { StatusCode = 200, LatencyMs = 126 }).ToList(),
            Processes = LatestSnapshot.Processes.Select(process => process with { Status = "online" }).ToList()
        },
        LatestSnapshot with
        {
            CapturedAt = DateTimeOffset.Parse("2026-05-10T09:48:00Z"),
            Metrics = LatestSnapshot.Metrics with { CpuPercent = 47, MemoryPercent = 61 },
            Endpoints = LatestSnapshot.Endpoints.Select(endpoint => endpoint with { StatusCode = 200, LatencyMs = 172 }).ToList(),
            Processes = LatestSnapshot.Processes.Select(process => process with { Status = "online" }).ToList()
        },
        LatestSnapshot with
        {
            CapturedAt = DateTimeOffset.Parse("2026-05-10T09:51:00Z"),
            Metrics = LatestSnapshot.Metrics with { CpuPercent = 54, MemoryPercent = 69 },
            Endpoints = LatestSnapshot.Endpoints.Select(endpoint => endpoint with { StatusCode = 200, LatencyMs = 214 }).ToList(),
            Processes = LatestSnapshot.Processes.Select(process => process with { Status = "online" }).ToList()
        },
        LatestSnapshot with
        {
            CapturedAt = DateTimeOffset.Parse("2026-05-10T09:54:00Z"),
            Metrics = LatestSnapshot.Metrics with { CpuPercent = 62, MemoryPercent = 74 },
            Endpoints = LatestSnapshot.Endpoints.Select(endpoint => endpoint with { StatusCode = 200, LatencyMs = 226 }).ToList(),
            Processes = LatestSnapshot.Processes.Select(process => process with { Status = "online" }).ToList()
        },
        LatestSnapshot with
        {
            CapturedAt = DateTimeOffset.Parse("2026-05-10T09:57:00Z"),
            Metrics = LatestSnapshot.Metrics with { CpuPercent = 68, MemoryPercent = 77 },
            Endpoints = LatestSnapshot.Endpoints.Select(endpoint => endpoint with { StatusCode = 200, LatencyMs = 480 }).ToList(),
            Processes = LatestSnapshot.Processes.Select(process => process.Name == "backend" ? process with { Restarts = 4 } : process).ToList()
        },
        LatestSnapshot
    ];
}
