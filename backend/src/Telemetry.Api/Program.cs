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

var databaseUrl = builder.Configuration["DATABASE_URL"];
ITelemetryStore store = string.IsNullOrWhiteSpace(databaseUrl)
    ? new InMemoryTelemetryStore()
    : new PostgresTelemetryStore(databaseUrl);

if (store is PostgresTelemetryStore postgresStore)
{
    await postgresStore.InitializeAsync(CancellationToken.None);
}
await EnsureDefaultRepositoriesAsync(store, builder.Configuration, CancellationToken.None);

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

app.MapGet("/repositories", async (CancellationToken cancellationToken) =>
{
    var repositories = await BuildRepositoryViewsAsync(store, cancellationToken);
    return Results.Ok(repositories);
});

app.MapPatch("/repositories/{id}/settings", async Task<IResult> (
    string id,
    UpdateRepositorySettingsRequest request,
    CancellationToken cancellationToken) =>
{
    var repository = await store.UpdateRepositorySettingsAsync(
        id,
        request.AutoDeployEnabled,
        DateTimeOffset.UtcNow,
        cancellationToken);

    return repository is null ? Results.NotFound() : Results.Ok(repository);
});

app.MapGet("/repositories/{id}/commits", async Task<IResult> (
    string id,
    int? limit,
    CancellationToken cancellationToken) =>
{
    var repository = await store.GetRepositoryAsync(id, cancellationToken);
    if (repository is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(await store.GetCommitsAsync(id, limit ?? 30, cancellationToken));
});

app.MapPost("/repositories/{id}/sync", async Task<IResult> (
    string id,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var repository = await store.GetRepositoryAsync(id, cancellationToken);
    if (repository is null)
    {
        return Results.NotFound();
    }

    var commits = await FetchGitHubCommitsAsync(
        httpClientFactory.CreateClient(),
        configuration["GITHUB_TOKEN"],
        repository,
        cancellationToken);
    await store.UpsertCommitsAsync(repository.Id, commits, cancellationToken);
    return Results.Ok(commits);
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

app.MapPost("/github/webhook", async Task<IResult> (
    HttpRequest request,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var secret = configuration["GITHUB_WEBHOOK_SECRET"];
    if (string.IsNullOrWhiteSpace(secret))
    {
        return Results.Problem("GITHUB_WEBHOOK_SECRET is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    if (!GitHubDeploymentPolicy.VerifySignature(body, request.Headers["X-Hub-Signature-256"].FirstOrDefault(), secret))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(request.Headers["X-GitHub-Event"].FirstOrDefault(), "push", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok(new { ignored = true, reason = "Only push events are supported." });
    }

    var push = ParseGitHubPushEvent(body);
    var repository = await store.GetRepositoryByFullNameAsync(push.RepositoryFullName, cancellationToken);
    if (repository is null)
    {
        return Results.NotFound(new { error = $"Repository '{push.RepositoryFullName}' is not registered." });
    }

    var commits = ParseGitHubCommits(body, repository.Id);
    if (commits.Count > 0)
    {
        await store.UpsertCommitsAsync(repository.Id, commits, cancellationToken);
    }

    var decision = GitHubDeploymentPolicy.EvaluatePush(repository, push);
    if (!decision.ShouldCreateCommand || decision.Action is null || push.HeadCommit is null)
    {
        return Results.Accepted($"/repositories/{repository.Id}/commits", new
        {
            repository,
            decision.Reason,
            command = (OpsCommand?)null
        });
    }

    var history = await store.GetCommandsAsync(repository.ProjectId, cancellationToken);
    var duplicate = history.FirstOrDefault(command =>
        string.Equals(command.TriggerRepositoryId, repository.Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(command.TriggerCommitSha, push.HeadCommit.Sha, StringComparison.OrdinalIgnoreCase));
    if (duplicate is not null)
    {
        return Results.Ok(new { repository, reason = "Duplicate webhook delivery ignored.", command = duplicate });
    }

    var commandDecision = CommandPolicy.CreateCommand(
        repository.ProjectId,
        CommandPolicy.ToWireName(decision.Action.Value),
        target: null,
        requestedBy: decision.RequestedBy,
        confirmation: repository.ProjectId,
        history: history,
        requestedAt: DateTimeOffset.UtcNow);

    if (!commandDecision.IsAccepted || commandDecision.Command is null)
    {
        return Results.BadRequest(new { error = commandDecision.Error });
    }

    var command = commandDecision.Command with
    {
        TriggerSource = "github_push",
        TriggerRepositoryId = repository.Id,
        TriggerCommitSha = push.HeadCommit.Sha,
        TriggerCommitMessage = push.HeadCommit.Message,
        TriggerCommitUrl = push.HeadCommit.Url
    };

    return Results.Accepted($"/commands/{command.Id}", new
    {
        repository,
        command = await store.CreateCommandAsync(command, cancellationToken)
    });
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

static async Task EnsureDefaultRepositoriesAsync(
    ITelemetryStore store,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var projectId = configuration["MONITORED_PROJECT_ID"] ?? "dukefarm";
    var repositories = new[]
    {
        GitRepository.CreateDefault("backend", configuration["DUKEFARM_BACKEND_REPO"] ?? "koard/DukeFarm-Backend", "main", projectId),
        GitRepository.CreateDefault("frontend", configuration["DUKEFARM_FRONTEND_REPO"] ?? "koard/DukeFarm-Frontend", "main", projectId),
        GitRepository.CreateDefault("admin", configuration["DUKEFARM_ADMIN_REPO"] ?? "koard/DukeFarm-Admin", "main", projectId)
    };

    foreach (var repository in repositories)
    {
        await store.UpsertRepositoryAsync(repository, preserveSettings: true, cancellationToken);
    }
}

static async Task<IReadOnlyList<RepositoryDeploymentView>> BuildRepositoryViewsAsync(
    ITelemetryStore store,
    CancellationToken cancellationToken)
{
    var repositories = await store.GetRepositoriesAsync(cancellationToken);
    var results = new List<RepositoryDeploymentView>();

    foreach (var repository in repositories)
    {
        var recentCommits = await store.GetCommitsAsync(repository.Id, 8, cancellationToken);
        var latestCommit = recentCommits.FirstOrDefault();
        var latestCommand = (await store.GetCommandsAsync(repository.ProjectId, cancellationToken))
            .Where(command => string.Equals(command.TriggerRepositoryId, repository.Id, StringComparison.OrdinalIgnoreCase) ||
                command.Action == repository.DeployAction)
            .OrderByDescending(command => command.RequestedAt)
            .FirstOrDefault();

        results.Add(new RepositoryDeploymentView(repository, latestCommit, latestCommand, recentCommits));
    }

    return results;
}

static GitHubPushEvent ParseGitHubPushEvent(string body)
{
    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;
    var repositoryFullName = root.GetProperty("repository").GetProperty("full_name").GetString() ?? "";
    var senderLogin = root.TryGetProperty("sender", out var sender)
        ? sender.GetProperty("login").GetString() ?? "unknown"
        : "unknown";
    var headCommit = TryParseCommit(root.GetProperty("head_commit"), repositoryId: "", senderLogin);

    return new GitHubPushEvent(
        RepositoryFullName: repositoryFullName,
        Ref: root.GetProperty("ref").GetString() ?? "",
        SenderLogin: senderLogin,
        HeadCommit: headCommit);
}

static IReadOnlyList<GitCommit> ParseGitHubCommits(string body, string repositoryId)
{
    using var document = JsonDocument.Parse(body);
    var root = document.RootElement;
    var senderLogin = root.TryGetProperty("sender", out var sender)
        ? sender.GetProperty("login").GetString() ?? "unknown"
        : "unknown";
    var commits = new List<GitCommit>();

    if (root.TryGetProperty("commits", out var commitsElement))
    {
        foreach (var commitElement in commitsElement.EnumerateArray())
        {
            var commit = TryParseCommit(commitElement, repositoryId, senderLogin);
            if (commit is not null)
            {
                commits.Add(commit);
            }
        }
    }

    if (root.TryGetProperty("head_commit", out var headCommitElement))
    {
        var headCommit = TryParseCommit(headCommitElement, repositoryId, senderLogin);
        if (headCommit is not null && commits.All(commit => commit.Sha != headCommit.Sha))
        {
            commits.Insert(0, headCommit);
        }
    }

    return commits;
}

static GitCommit? TryParseCommit(JsonElement commitElement, string repositoryId, string senderLogin)
{
    if (commitElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        return null;
    }

    var sha = commitElement.TryGetProperty("id", out var idElement)
        ? idElement.GetString()
        : commitElement.TryGetProperty("sha", out var shaElement)
            ? shaElement.GetString()
            : null;
    if (string.IsNullOrWhiteSpace(sha))
    {
        return null;
    }

    var author = commitElement.TryGetProperty("author", out var authorElement)
        ? authorElement
        : default;
    var authorName = author.ValueKind == JsonValueKind.Object && author.TryGetProperty("name", out var nameElement)
        ? nameElement.GetString() ?? senderLogin
        : senderLogin;
    var authorLogin = author.ValueKind == JsonValueKind.Object && author.TryGetProperty("username", out var usernameElement)
        ? usernameElement.GetString() ?? senderLogin
        : senderLogin;
    var timestamp = commitElement.TryGetProperty("timestamp", out var timestampElement) &&
        DateTimeOffset.TryParse(timestampElement.GetString(), out var parsedTimestamp)
            ? parsedTimestamp
            : DateTimeOffset.UtcNow;

    return new GitCommit(
        RepositoryId: repositoryId,
        Sha: sha,
        Message: commitElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? "" : "",
        AuthorName: authorName,
        AuthorLogin: authorLogin,
        Url: commitElement.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? "" : "",
        CommittedAt: timestamp);
}

static async Task<IReadOnlyList<GitCommit>> FetchGitHubCommitsAsync(
    HttpClient http,
    string? token,
    GitRepository repository,
    CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        $"https://api.github.com/repos/{repository.FullName}/commits?sha={Uri.EscapeDataString(repository.Branch)}&per_page=30");
    request.Headers.UserAgent.ParseAdd("OpsPulse/1.0");
    if (!string.IsNullOrWhiteSpace(token))
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    using var response = await http.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    var commits = new List<GitCommit>();

    foreach (var item in document.RootElement.EnumerateArray())
    {
        var sha = item.GetProperty("sha").GetString() ?? "";
        var commit = item.GetProperty("commit");
        var author = commit.GetProperty("author");
        var githubAuthor = item.TryGetProperty("author", out var userElement) && userElement.ValueKind == JsonValueKind.Object
            ? userElement
            : default;
        commits.Add(new GitCommit(
            RepositoryId: repository.Id,
            Sha: sha,
            Message: commit.GetProperty("message").GetString() ?? "",
            AuthorName: author.GetProperty("name").GetString() ?? "unknown",
            AuthorLogin: githubAuthor.ValueKind == JsonValueKind.Object && githubAuthor.TryGetProperty("login", out var loginElement)
                ? loginElement.GetString() ?? "unknown"
                : "unknown",
            Url: item.GetProperty("html_url").GetString() ?? "",
            CommittedAt: DateTimeOffset.Parse(author.GetProperty("date").GetString() ?? DateTimeOffset.UtcNow.ToString("O"))));
    }

    return commits;
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

public sealed record UpdateRepositorySettingsRequest(bool AutoDeployEnabled);

public static class ProjectRegistry
{
    public static IReadOnlyList<string> DefaultExpectedProcesses { get; } =
        ["dukefarm-backend", "dukefarm-frontend", "dukefarm-admin", "opspulse-agent"];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedProcessesByProject { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["dukefarm"] = DefaultExpectedProcesses
        };
}
