using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

var databaseUrl = builder.Configuration["DATABASE_URL"];
ITelemetryStore store = string.IsNullOrWhiteSpace(databaseUrl)
    ? new InMemoryTelemetryStore()
    : new PostgresTelemetryStore(databaseUrl);

if (store is PostgresTelemetryStore postgresStore)
{
    await postgresStore.InitializeAsync(CancellationToken.None);
}

var app = builder.Build();

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "telemetry" }));

app.MapPost("/snapshots", async (ProjectSnapshot snapshot, CancellationToken cancellationToken) =>
{
    var incidents = await ApplySnapshot(store, snapshot, cancellationToken);
    return Results.Accepted($"/snapshots/latest/{snapshot.ProjectId}", new { snapshot, incidents });
});

app.MapPost("/ingest/snapshots", async (HttpRequest request, ProjectSnapshot snapshot, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAuthorizedAgent(request, snapshot.ProjectId, configuration))
    {
        return Results.Unauthorized();
    }

    var snapshotWithAgent = snapshot.Agent is null
        ? snapshot with
        {
            Agent = new AgentMetadata(
                Version: request.Headers["X-Agent-Version"].FirstOrDefault() ?? "unknown",
                Hostname: request.Headers["X-Agent-Hostname"].FirstOrDefault() ?? "unknown",
                Os: request.Headers["X-Agent-Os"].FirstOrDefault() ?? "unknown",
                Pm2Version: request.Headers["X-Agent-Pm2-Version"].FirstOrDefault() ?? "unknown",
                ReceivedAt: DateTimeOffset.UtcNow)
        }
        : snapshot;

    var incidents = await ApplySnapshot(store, snapshotWithAgent, cancellationToken);
    return Results.Accepted($"/snapshots/latest/{snapshot.ProjectId}", new { snapshot = snapshotWithAgent, incidents });
});

app.MapGet("/snapshots/latest", async (CancellationToken cancellationToken) =>
{
    return Results.Ok(await store.GetLatestSnapshotsAsync(cancellationToken));
});

app.MapGet("/snapshots/latest/{projectId}", async Task<IResult> (string projectId, CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetLatestSnapshotAsync(projectId, cancellationToken);
    if (snapshot is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(snapshot);
});

app.MapGet("/snapshots/history/{projectId}", async (string projectId, CancellationToken cancellationToken) =>
{
    var history = await store.GetSnapshotHistoryAsync(projectId, cancellationToken);
    return Results.Ok(history);
});

app.MapGet("/incidents", async (string? projectId, CancellationToken cancellationToken) =>
{
    var incidents = await store.GetIncidentsAsync(projectId, cancellationToken);
    return Results.Ok(incidents);
});

app.MapPost("/incidents/{id}/acknowledge", async Task<IResult> (string id, CancellationToken cancellationToken) =>
{
    var incident = await store.UpdateIncidentStatusAsync(id, IncidentStatus.Acknowledged, DateTimeOffset.UtcNow, cancellationToken);
    return incident is null ? Results.NotFound() : Results.Ok(incident);
});

app.MapPost("/incidents/{id}/resolve", async Task<IResult> (string id, CancellationToken cancellationToken) =>
{
    var incident = await store.UpdateIncidentStatusAsync(id, IncidentStatus.Resolved, DateTimeOffset.UtcNow, cancellationToken);
    return incident is null ? Results.NotFound() : Results.Ok(incident);
});

app.MapGet("/slo/{projectId}", async Task<IResult> (string projectId, CancellationToken cancellationToken) =>
{
    var history = await store.GetSnapshotHistoryAsync(projectId, cancellationToken);
    if (history.Count == 0)
    {
        return Results.NotFound();
    }

    return Results.Ok(SloCalculator.Calculate(projectId, history, targetAvailabilityPercent: 99.9));
});

app.MapGet("/agents", async (CancellationToken cancellationToken) =>
{
    var snapshots = await store.GetLatestSnapshotsAsync(cancellationToken);
    var statuses = snapshots
        .Select(snapshot =>
        {
            var expected = ProjectRegistry.ExpectedProcessesByProject.TryGetValue(snapshot.ProjectId, out var processes)
                ? processes
                : ProjectRegistry.DefaultExpectedProcesses;

            return AgentPolicy.Evaluate(snapshot, expected, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(3));
        })
        .ToList();

    return Results.Ok(statuses);
});

app.MapGet("/commands", async Task<IResult> (string? projectId, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(projectId))
    {
        return Results.BadRequest(new { error = "projectId is required." });
    }

    return Results.Ok(await store.GetCommandsAsync(projectId, cancellationToken));
});

app.MapPost("/commands", async Task<IResult> (CreateCommandRequest request, CancellationToken cancellationToken) =>
{
    var history = await store.GetCommandsAsync(request.ProjectId, cancellationToken);
    var decision = CommandPolicy.CreateCommand(
        request.ProjectId,
        request.Action,
        request.Target,
        request.RequestedBy,
        request.Confirmation,
        history,
        DateTimeOffset.UtcNow);

    if (!decision.IsAccepted || decision.Command is null)
    {
        return Results.BadRequest(new { error = decision.Error });
    }

    return Results.Accepted($"/commands/{decision.Command.Id}", await store.CreateCommandAsync(decision.Command, cancellationToken));
});

app.MapPost("/agent/commands/claim", async Task<IResult> (
    HttpRequest httpRequest,
    ClaimCommandRequest request,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorizedAgent(httpRequest, request.ProjectId, configuration))
    {
        return Results.Unauthorized();
    }

    var command = await store.ClaimNextCommandAsync(request.ProjectId, DateTimeOffset.UtcNow, cancellationToken);
    return command is null ? Results.NoContent() : Results.Ok(command);
});

app.MapPost("/agent/commands/{id}/result", async Task<IResult> (
    string id,
    HttpRequest httpRequest,
    CommandResultRequest request,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!IsAuthorizedAgent(httpRequest, request.ProjectId, configuration))
    {
        return Results.Unauthorized();
    }

    if (!TryParseCommandStatus(request.Status, out var status))
    {
        return Results.BadRequest(new { error = $"Status '{request.Status}' is not supported." });
    }

    var command = await store.CompleteCommandAsync(
        id,
        status,
        request.Summary,
        request.Stdout,
        request.Stderr,
        request.ReleaseCommit,
        DateTimeOffset.UtcNow,
        cancellationToken);

    return command is null ? Results.NotFound() : Results.Ok(command);
});

app.Run();

static async Task<IReadOnlyList<Incident>> ApplySnapshot(
    ITelemetryStore store,
    ProjectSnapshot snapshot,
    CancellationToken cancellationToken)
{
    var existingIncidents = await store.GetIncidentsAsync(snapshot.ProjectId, cancellationToken);
    var incidents = IncidentPolicy.ApplyAlerts(existingIncidents, snapshot, AlertEvaluator.Evaluate(snapshot));
    await store.SaveSnapshotAsync(snapshot, incidents, cancellationToken);
    return incidents;
}

static bool IsAuthorizedAgent(HttpRequest request, string projectId, IConfiguration configuration)
{
    var expectedToken = configuration[$"AgentTokens:{projectId}"];
    if (string.IsNullOrWhiteSpace(expectedToken))
    {
        return false;
    }

    var providedToken = request.Headers["X-Agent-Token"].FirstOrDefault();
    return string.Equals(providedToken, expectedToken, StringComparison.Ordinal);
}

static bool TryParseCommandStatus(string value, out OpsCommandStatus status)
{
    var normalized = value.Replace("_", "", StringComparison.OrdinalIgnoreCase);
    return Enum.TryParse(normalized, ignoreCase: true, out status)
        && status is OpsCommandStatus.Succeeded
            or OpsCommandStatus.Failed
            or OpsCommandStatus.TimedOut
            or OpsCommandStatus.Cancelled;
}

public sealed record CreateCommandRequest(
    string ProjectId,
    string Action,
    string? Target,
    string RequestedBy,
    string Confirmation);

public sealed record ClaimCommandRequest(string ProjectId);

public sealed record CommandResultRequest(
    string ProjectId,
    string Status,
    string Summary,
    string? Stdout,
    string? Stderr,
    string? ReleaseCommit);

public static class ProjectRegistry
{
    public static IReadOnlyList<string> DefaultExpectedProcesses { get; } =
        ["dukefarm-backend", "dukefarm-frontend", "dukefarm-admin", "opspulse-agent"];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedProcessesByProject { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["dukefarm"] = DefaultExpectedProcesses,
            ["dukefarm-production"] = ["dukefarm-backend", "dukefarm-frontend", "dukefarm-admin", "opspulse-agent"]
        };
}
