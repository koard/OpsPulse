using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }));

app.MapGet("/api/dashboard", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var http = httpClientFactory.CreateClient();
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";

    var latestSnapshots = await ReadJsonOrDefault<IReadOnlyList<ProjectSnapshot>>(
        http,
        $"{telemetryUrl}/snapshots/latest",
        []);

    var dashboardProjects = new List<DashboardProject>();

    foreach (var snapshot in latestSnapshots)
    {
        var history = await ReadJsonOrDefault(
            http,
            $"{telemetryUrl}/snapshots/history/{Uri.EscapeDataString(snapshot.ProjectId)}",
            Array.Empty<ProjectSnapshot>());

        dashboardProjects.Add(ToDashboardProject(snapshot, history));
    }

    return Results.Ok(new DashboardPayload(DateTimeOffset.UtcNow, dashboardProjects));
});

app.MapGet("/api/incidents", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var incidents = await ReadJsonOrDefault(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/incidents",
        Array.Empty<Incident>());

    return Results.Ok(incidents);
});

app.MapPost("/api/incidents/{id}/acknowledge", async Task<IResult> (string id, IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var incident = await PostForJsonOrDefault<Incident>(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/incidents/{id}/acknowledge");

    return incident is null ? Results.NotFound() : Results.Ok(incident);
});

app.MapPost("/api/incidents/{id}/resolve", async Task<IResult> (string id, IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var incident = await PostForJsonOrDefault<Incident>(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/incidents/{id}/resolve");

    return incident is null ? Results.NotFound() : Results.Ok(incident);
});

app.MapGet("/api/slo/{projectId}", async Task<IResult> (string projectId, IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var report = await ReadJsonOrDefault<SloReport?>(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/slo/{projectId}",
        null);

    return report is null ? Results.NotFound() : Results.Ok(report);
});

app.MapGet("/api/agents", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var agents = await ReadJsonOrDefault(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/agents",
        Array.Empty<AgentStatus>());

    return Results.Ok(agents);
});

app.MapGet("/api/commands", async Task<IResult> (string? projectId, IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    if (string.IsNullOrWhiteSpace(projectId))
    {
        return Results.BadRequest(new { error = "projectId is required." });
    }

    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var commands = await ReadJsonOrDefault(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/commands?projectId={Uri.EscapeDataString(projectId)}",
        Array.Empty<OpsCommand>());

    return Results.Ok(commands);
});

app.MapGet("/api/repositories", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var repositories = await ReadJsonOrDefault(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/repositories",
        Array.Empty<RepositoryDeploymentView>());

    return Results.Ok(repositories);
});

app.MapPatch("/api/repositories/{id}/settings", async Task<IResult> (
    string id,
    UpdateRepositorySettingsRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var response = await PatchJsonAsync(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/repositories/{Uri.EscapeDataString(id)}/settings",
        request);
    var content = await response.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
});

app.MapGet("/api/repositories/{id}/commits", async Task<IResult> (
    string id,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var commits = await ReadJsonOrDefault(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/repositories/{Uri.EscapeDataString(id)}/commits",
        Array.Empty<GitCommit>());

    return Results.Ok(commits);
});

app.MapPost("/api/repositories/{id}/sync", async Task<IResult> (
    string id,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var response = await httpClientFactory.CreateClient().PostAsync(
        $"{telemetryUrl}/repositories/{Uri.EscapeDataString(id)}/sync",
        content: null);
    var content = await response.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/commands", async Task<IResult> (
    CreateCommandRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    var response = await PostJsonAsync(
        httpClientFactory.CreateClient(),
        $"{telemetryUrl}/commands",
        request);

    var content = await response.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/github/webhook", async Task<IResult> (
    HttpRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) =>
{
    var telemetryUrl = configuration["Services:Telemetry"] ?? "http://localhost:5082";
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{telemetryUrl}/github/webhook")
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    foreach (var header in new[] { "X-GitHub-Event", "X-GitHub-Delivery", "X-Hub-Signature-256" })
    {
        if (request.Headers.TryGetValue(header, out var value))
        {
            httpRequest.Headers.TryAddWithoutValidation(header, value.ToArray());
        }
    }

    var response = await httpClientFactory.CreateClient().SendAsync(httpRequest);
    var content = await response.Content.ReadAsStringAsync();
    return Results.Content(content, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
});

app.Run();

static async Task<T> ReadJsonOrDefault<T>(HttpClient http, string url, T fallback)
{
    try
    {
        var value = await http.GetFromJsonAsync<T>(url, GatewayJson.Options);
        return value ?? fallback;
    }
    catch
    {
        return fallback;
    }
}

static async Task<T?> PostForJsonOrDefault<T>(HttpClient http, string url)
{
    try
    {
        var response = await http.PostAsync(url, content: null);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(GatewayJson.Options);
    }
    catch
    {
        return default;
    }
}

static async Task<HttpResponseMessage> PostJsonAsync<T>(HttpClient http, string url, T payload)
{
    try
    {
        return await http.PostAsJsonAsync(url, payload, GatewayJson.Options);
    }
    catch
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);
    }
}

static async Task<HttpResponseMessage> PatchJsonAsync<T>(HttpClient http, string url, T payload)
{
    try
    {
        return await http.PatchAsJsonAsync(url, payload, GatewayJson.Options);
    }
    catch
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);
    }
}

static DashboardProject ToDashboardProject(
    ProjectSnapshot snapshot,
    IReadOnlyList<ProjectSnapshot> history)
{
    var alerts = AlertEvaluator.Evaluate(snapshot).ToList();
    var timeline = history.Select(item =>
    {
        var report = HealthPolicy.Calculate(item);
        var latency = item.Endpoints.Count == 0
            ? 0
            : Convert.ToInt32(item.Endpoints.Average(endpoint => endpoint.LatencyMs));

        return new TimelinePoint(item.CapturedAt.ToString("HH:mm"), report.Score, latency);
    }).ToList();

    return new DashboardProject(
        Id: snapshot.ProjectId,
        Name: ProjectPresentation.ToDisplayName(snapshot.ProjectId),
        Environment: ProjectPresentation.ToEnvironment(snapshot.ProjectId),
        Owner: "",
        Server: new ServerProfile(
            Hostname: snapshot.Agent?.Hostname ?? snapshot.ProjectId,
            Os: snapshot.Agent?.Os ?? "unknown",
            Access: "Public HTTPS ingest",
            ReverseProxy: "Nginx",
            ProcessManager: "PM2"),
        Endpoints: snapshot.Endpoints,
        Processes: snapshot.Processes,
        Metrics: snapshot.Metrics,
        Alerts: alerts,
        Timeline: timeline);
}

public sealed record DashboardPayload(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DashboardProject> Projects);

public sealed record DashboardProject(
    string Id,
    string Name,
    string Environment,
    string Owner,
    ServerProfile Server,
    IReadOnlyList<EndpointCheck> Endpoints,
    IReadOnlyList<ProcessMetric> Processes,
    HostMetrics Metrics,
    IReadOnlyList<AlertEvent> Alerts,
    IReadOnlyList<TimelinePoint> Timeline);

public static class GatewayJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed record CreateCommandRequest(
    string ProjectId,
    string Action,
    string? Target,
    string RequestedBy,
    string Confirmation);

public sealed record UpdateRepositorySettingsRequest(bool AutoDeployEnabled);
