namespace Platform.Domain;

public static class CommandPolicy
{
    public const int OutputTailLimit = 4096;

    private static readonly IReadOnlyDictionary<string, OpsCommandAction> ActionByWireName =
        new Dictionary<string, OpsCommandAction>(StringComparer.OrdinalIgnoreCase)
        {
            ["health_check_now"] = OpsCommandAction.HealthCheckNow,
            ["pm2_restart_process"] = OpsCommandAction.Pm2RestartProcess,
            ["redeploy_backend"] = OpsCommandAction.RedeployBackend,
            ["redeploy_frontend"] = OpsCommandAction.RedeployFrontend,
            ["redeploy_admin"] = OpsCommandAction.RedeployAdmin,
            ["prisma_migrate_deploy"] = OpsCommandAction.PrismaMigrateDeploy,
            ["rollback_backend"] = OpsCommandAction.RollbackBackend,
            ["rollback_frontend"] = OpsCommandAction.RollbackFrontend,
            ["rollback_admin"] = OpsCommandAction.RollbackAdmin
        };

    public static IReadOnlySet<string> AllowedPm2Processes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dukefarm-backend",
            "dukefarm-admin",
            "dukefarm-frontend",
            "opspulse-agent"
        };

    public static CommandDecision CreateCommand(
        string projectId,
        string action,
        string? target,
        string requestedBy,
        string confirmation,
        IReadOnlyList<OpsCommand> history,
        DateTimeOffset requestedAt)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return Reject("Project id is required.");
        }

        if (!ActionByWireName.TryGetValue(action, out var parsedAction))
        {
            return Reject($"Action '{action}' is not allowlisted.");
        }

        var normalizedTarget = ResolveTarget(parsedAction, target, history);
        if (normalizedTarget.Error is not null)
        {
            return Reject(normalizedTarget.Error);
        }

        var expectedConfirmation = parsedAction == OpsCommandAction.Pm2RestartProcess
            ? normalizedTarget.Target
            : projectId;
        if (!string.Equals(confirmation, expectedConfirmation, StringComparison.Ordinal))
        {
            return Reject($"Confirmation must match '{expectedConfirmation}'.");
        }

        return new CommandDecision(
            IsAccepted: true,
            Command: new OpsCommand(
                Id: StableCommandId(projectId, parsedAction, normalizedTarget.Target, requestedAt),
                ProjectId: projectId,
                Action: parsedAction,
                Target: normalizedTarget.Target,
                Status: OpsCommandStatus.Pending,
                RequestedBy: string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : requestedBy,
                RequestedAt: requestedAt),
            Error: null);
    }

    public static OpsCommand Claim(OpsCommand command, DateTimeOffset claimedAt)
    {
        return command.Status == OpsCommandStatus.Pending
            ? command with { Status = OpsCommandStatus.Claimed, ClaimedAt = claimedAt }
            : command;
    }

    public static OpsCommand Complete(
        OpsCommand command,
        OpsCommandStatus status,
        string summary,
        string? stdout,
        string? stderr,
        string? releaseCommit,
        DateTimeOffset finishedAt)
    {
        if (!IsTerminal(status))
        {
            throw new ArgumentException("Command result status must be terminal.", nameof(status));
        }

        return command with
        {
            Status = status,
            FinishedAt = finishedAt,
            Summary = summary,
            StdoutTail = Tail(stdout),
            StderrTail = Tail(stderr),
            ReleaseCommit = string.IsNullOrWhiteSpace(releaseCommit) ? command.ReleaseCommit : releaseCommit
        };
    }

    public static string ToWireName(OpsCommandAction action)
    {
        return action switch
        {
            OpsCommandAction.HealthCheckNow => "health_check_now",
            OpsCommandAction.Pm2RestartProcess => "pm2_restart_process",
            OpsCommandAction.RedeployBackend => "redeploy_backend",
            OpsCommandAction.RedeployFrontend => "redeploy_frontend",
            OpsCommandAction.RedeployAdmin => "redeploy_admin",
            OpsCommandAction.PrismaMigrateDeploy => "prisma_migrate_deploy",
            OpsCommandAction.RollbackBackend => "rollback_backend",
            OpsCommandAction.RollbackFrontend => "rollback_frontend",
            OpsCommandAction.RollbackAdmin => "rollback_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private static (string Target, string? Error) ResolveTarget(
        OpsCommandAction action,
        string? target,
        IReadOnlyList<OpsCommand> history)
    {
        return action switch
        {
            OpsCommandAction.HealthCheckNow => ("dukefarm-production", null),
            OpsCommandAction.RedeployBackend => ("dukefarm-backend", null),
            OpsCommandAction.RedeployFrontend => ("dukefarm-frontend", null),
            OpsCommandAction.RedeployAdmin => ("dukefarm-admin", null),
            OpsCommandAction.PrismaMigrateDeploy => ("dukefarm-backend", null),
            OpsCommandAction.Pm2RestartProcess => ResolvePm2Target(target),
            OpsCommandAction.RollbackBackend => ResolveRollbackTarget(history, OpsCommandAction.RedeployBackend),
            OpsCommandAction.RollbackFrontend => ResolveRollbackTarget(history, OpsCommandAction.RedeployFrontend),
            OpsCommandAction.RollbackAdmin => ResolveRollbackTarget(history, OpsCommandAction.RedeployAdmin),
            _ => ("", "Unsupported command action.")
        };
    }

    private static (string Target, string? Error) ResolvePm2Target(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return ("", "Process target is required.");
        }

        return AllowedPm2Processes.Contains(target)
            ? (target, null)
            : (target, $"Process '{target}' is not allowlisted.");
    }

    private static (string Target, string? Error) ResolveRollbackTarget(
        IReadOnlyList<OpsCommand> history,
        OpsCommandAction redeployAction)
    {
        var previousCommit = history
            .Where(command =>
                command.Action == redeployAction &&
                command.Status == OpsCommandStatus.Succeeded &&
                !string.IsNullOrWhiteSpace(command.ReleaseCommit))
            .OrderByDescending(command => command.FinishedAt ?? command.RequestedAt)
            .FirstOrDefault()
            ?.ReleaseCommit;

        return previousCommit is null
            ? ("", $"No successful {ToWireName(redeployAction)} commit is available for rollback.")
            : (previousCommit, null);
    }

    private static bool IsTerminal(OpsCommandStatus status)
    {
        return status is OpsCommandStatus.Succeeded
            or OpsCommandStatus.Failed
            or OpsCommandStatus.TimedOut
            or OpsCommandStatus.Cancelled;
    }

    private static string? Tail(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= OutputTailLimit
            ? value
            : value[^OutputTailLimit..];
    }

    private static CommandDecision Reject(string error)
    {
        return new CommandDecision(false, null, error);
    }

    private static string StableCommandId(
        string projectId,
        OpsCommandAction action,
        string target,
        DateTimeOffset requestedAt)
    {
        var input = $"{projectId}:{action}:{target}:{requestedAt:O}:{Guid.NewGuid():N}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input)))[..16].ToLowerInvariant();
    }
}
