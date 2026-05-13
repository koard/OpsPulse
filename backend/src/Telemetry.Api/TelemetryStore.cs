using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Platform.Domain;

public interface ITelemetryStore
{
    Task SaveSnapshotAsync(ProjectSnapshot snapshot, IReadOnlyList<Incident> incidents, CancellationToken cancellationToken);

    Task<ProjectSnapshot?> GetLatestSnapshotAsync(string projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectSnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectSnapshot>> GetSnapshotHistoryAsync(string projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Incident>> GetIncidentsAsync(string? projectId, CancellationToken cancellationToken);

    Task<Incident?> UpdateIncidentStatusAsync(
        string incidentId,
        IncidentStatus status,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentStatus>> GetAgentStatusesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectedProcessesByProject,
        DateTimeOffset now,
        TimeSpan staleAfter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OpsCommand>> GetCommandsAsync(string projectId, CancellationToken cancellationToken);

    Task<OpsCommand> CreateCommandAsync(OpsCommand command, CancellationToken cancellationToken);

    Task<OpsCommand?> ClaimNextCommandAsync(string projectId, DateTimeOffset claimedAt, CancellationToken cancellationToken);

    Task<OpsCommand?> CompleteCommandAsync(
        string commandId,
        OpsCommandStatus status,
        string summary,
        string? stdout,
        string? stderr,
        string? releaseCommit,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken);
}

public static class TelemetryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed class InMemoryTelemetryStore : ITelemetryStore
{
    private readonly ConcurrentDictionary<string, List<ProjectSnapshot>> snapshots = new();
    private readonly ConcurrentDictionary<string, Incident> incidents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, OpsCommand> commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<CommandAuditEntry> commandAudit = [];

    public Task SaveSnapshotAsync(ProjectSnapshot snapshot, IReadOnlyList<Incident> updatedIncidents, CancellationToken cancellationToken)
    {
        var history = snapshots.GetOrAdd(snapshot.ProjectId, _ => []);

        lock (history)
        {
            history.Add(snapshot);
            history.Sort((left, right) => left.CapturedAt.CompareTo(right.CapturedAt));
        }

        foreach (var incident in updatedIncidents)
        {
            incidents[incident.Id] = incident;
        }

        return Task.CompletedTask;
    }

    public Task<ProjectSnapshot?> GetLatestSnapshotAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!snapshots.TryGetValue(projectId, out var history) || history.Count == 0)
        {
            return Task.FromResult<ProjectSnapshot?>(null);
        }

        lock (history)
        {
            return Task.FromResult<ProjectSnapshot?>(history[^1]);
        }
    }

    public Task<IReadOnlyList<ProjectSnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken)
    {
        var latest = snapshots.Values
            .Select(history =>
            {
                lock (history)
                {
                    return history.Count == 0 ? null : history[^1];
                }
            })
            .OfType<ProjectSnapshot>()
            .OrderBy(snapshot => snapshot.ProjectId)
            .ToList();

        return Task.FromResult<IReadOnlyList<ProjectSnapshot>>(latest);
    }

    public Task<IReadOnlyList<ProjectSnapshot>> GetSnapshotHistoryAsync(string projectId, CancellationToken cancellationToken)
    {
        if (!snapshots.TryGetValue(projectId, out var history))
        {
            return Task.FromResult<IReadOnlyList<ProjectSnapshot>>([]);
        }

        lock (history)
        {
            return Task.FromResult<IReadOnlyList<ProjectSnapshot>>(history.ToArray());
        }
    }

    public Task<IReadOnlyList<Incident>> GetIncidentsAsync(string? projectId, CancellationToken cancellationToken)
    {
        var result = incidents.Values
            .Where(incident => projectId is null || string.Equals(incident.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(incident => incident.Status == IncidentStatus.Open)
            .ThenByDescending(incident => incident.LastSeenAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<Incident>>(result);
    }

    public Task<Incident?> UpdateIncidentStatusAsync(
        string incidentId,
        IncidentStatus status,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        if (!incidents.TryGetValue(incidentId, out var incident))
        {
            return Task.FromResult<Incident?>(null);
        }

        var updated = status switch
        {
            IncidentStatus.Acknowledged => incident with
            {
                Status = IncidentStatus.Acknowledged,
                AcknowledgedAt = changedAt
            },
            IncidentStatus.Resolved => incident with
            {
                Status = IncidentStatus.Resolved,
                ResolvedAt = changedAt
            },
            _ => incident with { Status = IncidentStatus.Open, ResolvedAt = null }
        };

        incidents[incidentId] = updated;
        return Task.FromResult<Incident?>(updated);
    }

    public Task<IReadOnlyList<AgentStatus>> GetAgentStatusesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectedProcessesByProject,
        DateTimeOffset now,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var statuses = snapshots
            .Select(entry =>
            {
                var expected = expectedProcessesByProject.TryGetValue(entry.Key, out var processes)
                    ? processes
                    : [];

                lock (entry.Value)
                {
                    return entry.Value.Count == 0
                        ? null
                        : AgentPolicy.Evaluate(entry.Value[^1], expected, now, staleAfter);
                }
            })
            .OfType<AgentStatus>()
            .ToList();

        return Task.FromResult<IReadOnlyList<AgentStatus>>(statuses);
    }

    public Task<IReadOnlyList<OpsCommand>> GetCommandsAsync(string projectId, CancellationToken cancellationToken)
    {
        var result = commands.Values
            .Where(command => string.Equals(command.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(command => command.RequestedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<OpsCommand>>(result);
    }

    public Task<OpsCommand> CreateCommandAsync(OpsCommand command, CancellationToken cancellationToken)
    {
        commands[command.Id] = command;
        AddAudit(command, "created", $"{command.Action} requested for {command.Target}.");
        return Task.FromResult(command);
    }

    public Task<OpsCommand?> ClaimNextCommandAsync(string projectId, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        var pending = commands.Values
            .Where(command =>
                string.Equals(command.ProjectId, projectId, StringComparison.OrdinalIgnoreCase) &&
                command.Status == OpsCommandStatus.Pending)
            .OrderBy(command => command.RequestedAt)
            .FirstOrDefault();

        if (pending is null)
        {
            return Task.FromResult<OpsCommand?>(null);
        }

        var claimed = CommandPolicy.Claim(pending, claimedAt);
        commands[claimed.Id] = claimed;
        AddAudit(claimed, "claimed", "Agent claimed command.");
        return Task.FromResult<OpsCommand?>(claimed);
    }

    public Task<OpsCommand?> CompleteCommandAsync(
        string commandId,
        OpsCommandStatus status,
        string summary,
        string? stdout,
        string? stderr,
        string? releaseCommit,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        if (!commands.TryGetValue(commandId, out var command))
        {
            return Task.FromResult<OpsCommand?>(null);
        }

        var completed = CommandPolicy.Complete(command, status, summary, stdout, stderr, releaseCommit, finishedAt);
        commands[completed.Id] = completed;
        AddAudit(completed, completed.Status.ToString(), completed.Summary ?? "Command completed.");
        return Task.FromResult<OpsCommand?>(completed);
    }

    private void AddAudit(OpsCommand command, string @event, string message)
    {
        commandAudit.Add(new CommandAuditEntry(
            Id: Guid.NewGuid().ToString("N"),
            CommandId: command.Id,
            ProjectId: command.ProjectId,
            Event: @event,
            CreatedAt: DateTimeOffset.UtcNow,
            Message: message));
    }
}

public sealed class PostgresTelemetryStore(string connectionString) : ITelemetryStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            create table if not exists snapshots (
                project_id text not null,
                captured_at timestamptz not null,
                payload jsonb not null,
                primary key (project_id, captured_at)
            );

            create table if not exists incidents (
                id text primary key,
                project_id text not null,
                fingerprint text not null unique,
                status text not null,
                last_seen_at timestamptz not null,
                payload jsonb not null
            );

            create table if not exists agents (
                project_id text primary key,
                last_seen_at timestamptz not null,
                payload jsonb not null
            );

            create table if not exists commands (
                id text primary key,
                project_id text not null,
                action text not null,
                target text not null,
                status text not null,
                requested_at timestamptz not null,
                payload jsonb not null
            );

            create table if not exists command_audit (
                id text primary key,
                command_id text not null,
                project_id text not null,
                event text not null,
                created_at timestamptz not null,
                message text not null
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSnapshotAsync(ProjectSnapshot snapshot, IReadOnlyList<Incident> incidents, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = new NpgsqlCommand("""
            insert into snapshots (project_id, captured_at, payload)
            values (@project_id, @captured_at, @payload::jsonb)
            on conflict (project_id, captured_at) do update set payload = excluded.payload;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", snapshot.ProjectId);
            command.Parameters.AddWithValue("captured_at", snapshot.CapturedAt);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(snapshot, TelemetryJson.Options));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (snapshot.Agent is not null)
        {
            await using var command = new NpgsqlCommand("""
                insert into agents (project_id, last_seen_at, payload)
                values (@project_id, @last_seen_at, @payload::jsonb)
                on conflict (project_id) do update
                set last_seen_at = excluded.last_seen_at,
                    payload = excluded.payload;
                """, connection, transaction);
            command.Parameters.AddWithValue("project_id", snapshot.ProjectId);
            command.Parameters.AddWithValue("last_seen_at", snapshot.Agent.ReceivedAt);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(snapshot.Agent, TelemetryJson.Options));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var incident in incidents)
        {
            await using var command = new NpgsqlCommand("""
                insert into incidents (id, project_id, fingerprint, status, last_seen_at, payload)
                values (@id, @project_id, @fingerprint, @status, @last_seen_at, @payload::jsonb)
                on conflict (id) do update
                set status = excluded.status,
                    last_seen_at = excluded.last_seen_at,
                    payload = excluded.payload;
                """, connection, transaction);
            command.Parameters.AddWithValue("id", incident.Id);
            command.Parameters.AddWithValue("project_id", incident.ProjectId);
            command.Parameters.AddWithValue("fingerprint", incident.Fingerprint);
            command.Parameters.AddWithValue("status", incident.Status.ToString());
            command.Parameters.AddWithValue("last_seen_at", incident.LastSeenAt);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(incident, TelemetryJson.Options));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProjectSnapshot?> GetLatestSnapshotAsync(string projectId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select payload from snapshots
            where project_id = @project_id
            order by captured_at desc
            limit 1;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : JsonSerializer.Deserialize<ProjectSnapshot>(payload, TelemetryJson.Options);
    }

    public async Task<IReadOnlyList<ProjectSnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken)
    {
        var results = new List<ProjectSnapshot>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select distinct on (project_id) payload
            from snapshots
            order by project_id, captured_at desc;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var snapshot = JsonSerializer.Deserialize<ProjectSnapshot>(reader.GetString(0), TelemetryJson.Options);
            if (snapshot is not null)
            {
                results.Add(snapshot);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ProjectSnapshot>> GetSnapshotHistoryAsync(string projectId, CancellationToken cancellationToken)
    {
        var results = new List<ProjectSnapshot>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select payload from snapshots
            where project_id = @project_id
            order by captured_at;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var snapshot = JsonSerializer.Deserialize<ProjectSnapshot>(reader.GetString(0), TelemetryJson.Options);
            if (snapshot is not null)
            {
                results.Add(snapshot);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<Incident>> GetIncidentsAsync(string? projectId, CancellationToken cancellationToken)
    {
        var results = new List<Incident>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var sql = projectId is null
            ? "select payload from incidents order by status, last_seen_at desc;"
            : "select payload from incidents where project_id = @project_id order by status, last_seen_at desc;";
        await using var command = new NpgsqlCommand(sql, connection);
        if (projectId is not null)
        {
            command.Parameters.AddWithValue("project_id", projectId);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var incident = JsonSerializer.Deserialize<Incident>(reader.GetString(0), TelemetryJson.Options);
            if (incident is not null)
            {
                results.Add(incident);
            }
        }

        return results;
    }

    public async Task<Incident?> UpdateIncidentStatusAsync(
        string incidentId,
        IncidentStatus status,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var incident = (await GetIncidentsAsync(null, cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Id, incidentId, StringComparison.OrdinalIgnoreCase));
        if (incident is null)
        {
            return null;
        }

        var updated = status switch
        {
            IncidentStatus.Acknowledged => incident with { Status = status, AcknowledgedAt = changedAt },
            IncidentStatus.Resolved => incident with { Status = status, ResolvedAt = changedAt },
            _ => incident with { Status = IncidentStatus.Open, ResolvedAt = null }
        };

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            update incidents
            set status = @status,
                payload = @payload::jsonb
            where id = @id;
            """, connection);
        command.Parameters.AddWithValue("id", updated.Id);
        command.Parameters.AddWithValue("status", updated.Status.ToString());
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(updated, TelemetryJson.Options));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<AgentStatus>> GetAgentStatusesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> expectedProcessesByProject,
        DateTimeOffset now,
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var statuses = new List<AgentStatus>();
        foreach (var projectId in expectedProcessesByProject.Keys)
        {
            var latest = await GetLatestSnapshotAsync(projectId, cancellationToken);
            if (latest is null)
            {
                continue;
            }

            statuses.Add(AgentPolicy.Evaluate(latest, expectedProcessesByProject[projectId], now, staleAfter));
        }

        return statuses;
    }

    public async Task<IReadOnlyList<OpsCommand>> GetCommandsAsync(string projectId, CancellationToken cancellationToken)
    {
        var results = new List<OpsCommand>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            select payload from commands
            where project_id = @project_id
            order by requested_at desc;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = JsonSerializer.Deserialize<OpsCommand>(reader.GetString(0), TelemetryJson.Options);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    public async Task<OpsCommand> CreateCommandAsync(OpsCommand command, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertCommandAsync(connection, transaction, command, cancellationToken);
        await InsertAuditAsync(connection, transaction, command, "created", $"{command.Action} requested for {command.Target}.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return command;
    }

    public async Task<OpsCommand?> ClaimNextCommandAsync(string projectId, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        OpsCommand? pending = null;
        await using (var select = new NpgsqlCommand("""
            select payload from commands
            where project_id = @project_id and status = @status
            order by requested_at
            limit 1
            for update skip locked;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("project_id", projectId);
            select.Parameters.AddWithValue("status", OpsCommandStatus.Pending.ToString());
            var payload = await select.ExecuteScalarAsync(cancellationToken) as string;
            if (payload is not null)
            {
                pending = JsonSerializer.Deserialize<OpsCommand>(payload, TelemetryJson.Options);
            }
        }

        if (pending is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var claimed = CommandPolicy.Claim(pending, claimedAt);
        await UpsertCommandAsync(connection, transaction, claimed, cancellationToken);
        await InsertAuditAsync(connection, transaction, claimed, "claimed", "Agent claimed command.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    public async Task<OpsCommand?> CompleteCommandAsync(
        string commandId,
        OpsCommandStatus status,
        string summary,
        string? stdout,
        string? stderr,
        string? releaseCommit,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        OpsCommand? current = null;
        await using (var select = new NpgsqlCommand("""
            select payload from commands
            where id = @id
            for update;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("id", commandId);
            var payload = await select.ExecuteScalarAsync(cancellationToken) as string;
            if (payload is not null)
            {
                current = JsonSerializer.Deserialize<OpsCommand>(payload, TelemetryJson.Options);
            }
        }

        if (current is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var completed = CommandPolicy.Complete(current, status, summary, stdout, stderr, releaseCommit, finishedAt);
        await UpsertCommandAsync(connection, transaction, completed, cancellationToken);
        await InsertAuditAsync(connection, transaction, completed, completed.Status.ToString(), completed.Summary ?? "Command completed.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return completed;
    }

    private static async Task UpsertCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OpsCommand command,
        CancellationToken cancellationToken)
    {
        await using var upsert = new NpgsqlCommand("""
            insert into commands (id, project_id, action, target, status, requested_at, payload)
            values (@id, @project_id, @action, @target, @status, @requested_at, @payload::jsonb)
            on conflict (id) do update
            set status = excluded.status,
                payload = excluded.payload;
            """, connection, transaction);
        upsert.Parameters.AddWithValue("id", command.Id);
        upsert.Parameters.AddWithValue("project_id", command.ProjectId);
        upsert.Parameters.AddWithValue("action", command.Action.ToString());
        upsert.Parameters.AddWithValue("target", command.Target);
        upsert.Parameters.AddWithValue("status", command.Status.ToString());
        upsert.Parameters.AddWithValue("requested_at", command.RequestedAt);
        upsert.Parameters.AddWithValue("payload", JsonSerializer.Serialize(command, TelemetryJson.Options));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OpsCommand command,
        string @event,
        string message,
        CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            insert into command_audit (id, command_id, project_id, event, created_at, message)
            values (@id, @command_id, @project_id, @event, @created_at, @message);
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("command_id", command.Id);
        insert.Parameters.AddWithValue("project_id", command.ProjectId);
        insert.Parameters.AddWithValue("event", @event);
        insert.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        insert.Parameters.AddWithValue("message", message);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
