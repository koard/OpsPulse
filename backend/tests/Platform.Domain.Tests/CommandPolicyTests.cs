using Platform.Domain;

namespace Platform.Domain.Tests;

public sealed class CommandPolicyTests
{
    [Fact]
    public void CreateCommand_RejectsUnknownProcessRestartTarget()
    {
        var result = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: "pm2_restart_process",
            target: "database",
            requestedBy: "dashboard-user",
            confirmation: "database",
            history: [],
            requestedAt: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

        Assert.False(result.IsAccepted);
        Assert.Equal("Process 'database' is not allowlisted.", result.Error);
        Assert.Null(result.Command);
    }

    [Fact]
    public void CreateCommand_RequiresConfirmationForDangerousAction()
    {
        var result = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: "redeploy_backend",
            target: null,
            requestedBy: "dashboard-user",
            confirmation: "wrong-project",
            history: [],
            requestedAt: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

        Assert.False(result.IsAccepted);
        Assert.Equal("Confirmation must match 'dukefarm'.", result.Error);
    }

    [Fact]
    public void CreateCommand_CreatesPendingRedeployCommand()
    {
        var result = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: "redeploy_backend",
            target: null,
            requestedBy: "dashboard-user",
            confirmation: "dukefarm",
            history: [],
            requestedAt: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Command);
        Assert.Equal(OpsCommandAction.RedeployBackend, result.Command.Action);
        Assert.Equal(OpsCommandStatus.Pending, result.Command.Status);
        Assert.Equal("dukefarm-backend", result.Command.Target);
    }

    [Theory]
    [InlineData("redeploy_frontend", OpsCommandAction.RedeployFrontend, "dukefarm-frontend")]
    [InlineData("redeploy_admin", OpsCommandAction.RedeployAdmin, "dukefarm-admin")]
    public void CreateCommand_CreatesPendingFrontendAndAdminRedeployCommands(
        string action,
        OpsCommandAction expectedAction,
        string expectedTarget)
    {
        var result = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: action,
            target: null,
            requestedBy: "dashboard-user",
            confirmation: "dukefarm",
            history: [],
            requestedAt: DateTimeOffset.Parse("2026-05-12T10:00:00Z"));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Command);
        Assert.Equal(expectedAction, result.Command.Action);
        Assert.Equal(expectedTarget, result.Command.Target);
    }

    [Fact]
    public void CreateCommand_RollbackUsesLatestSuccessfulRedeployCommit()
    {
        var older = SeedCommand("old", DateTimeOffset.Parse("2026-05-12T09:00:00Z"), "abc111");
        var newer = SeedCommand("new", DateTimeOffset.Parse("2026-05-12T10:00:00Z"), "def222");

        var result = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: "rollback_backend",
            target: null,
            requestedBy: "dashboard-user",
            confirmation: "dukefarm",
            history: [older, newer],
            requestedAt: DateTimeOffset.Parse("2026-05-12T11:00:00Z"));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Command);
        Assert.Equal(OpsCommandAction.RollbackBackend, result.Command.Action);
        Assert.Equal("def222", result.Command.Target);
    }

    [Fact]
    public void CreateCommand_FrontendRollbackUsesLatestSuccessfulFrontendCommit()
    {
        var backend = SeedCommand("backend", DateTimeOffset.Parse("2026-05-12T10:00:00Z"), "backend111");
        var frontend = SeedCommand(
            "frontend",
            DateTimeOffset.Parse("2026-05-12T09:00:00Z"),
            "frontend222",
            OpsCommandAction.RedeployFrontend,
            "dukefarm-frontend");

        var result = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: "rollback_frontend",
            target: null,
            requestedBy: "dashboard-user",
            confirmation: "dukefarm",
            history: [backend, frontend],
            requestedAt: DateTimeOffset.Parse("2026-05-12T11:00:00Z"));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Command);
        Assert.Equal(OpsCommandAction.RollbackFrontend, result.Command.Action);
        Assert.Equal("frontend222", result.Command.Target);
    }

    [Fact]
    public void CompleteCommand_TruncatesOutputAndSetsTerminalState()
    {
        var command = CommandPolicy.CreateCommand(
            projectId: "dukefarm",
            action: "health_check_now",
            target: null,
            requestedBy: "dashboard-user",
            confirmation: "dukefarm",
            history: [],
            requestedAt: DateTimeOffset.Parse("2026-05-12T10:00:00Z")).Command!;
        var claimed = CommandPolicy.Claim(command, DateTimeOffset.Parse("2026-05-12T10:00:05Z"));

        var completed = CommandPolicy.Complete(
            claimed,
            OpsCommandStatus.Succeeded,
            summary: "health ok",
            stdout: new string('x', 5_000),
            stderr: "",
            releaseCommit: null,
            finishedAt: DateTimeOffset.Parse("2026-05-12T10:00:15Z"));

        Assert.Equal(OpsCommandStatus.Succeeded, completed.Status);
        Assert.Equal("health ok", completed.Summary);
        Assert.True(completed.StdoutTail!.Length <= 4_096);
        Assert.NotNull(completed.FinishedAt);
    }

    private static OpsCommand SeedCommand(
        string id,
        DateTimeOffset requestedAt,
        string commit,
        OpsCommandAction action = OpsCommandAction.RedeployBackend,
        string target = "dukefarm-backend")
    {
        return new OpsCommand(
            Id: id,
            ProjectId: "dukefarm",
            Action: action,
            Target: target,
            Status: OpsCommandStatus.Succeeded,
            RequestedBy: "dashboard-user",
            RequestedAt: requestedAt,
            ClaimedAt: requestedAt.AddSeconds(2),
            FinishedAt: requestedAt.AddMinutes(2),
            Summary: "deploy ok",
            StdoutTail: null,
            StderrTail: null,
            ReleaseCommit: commit);
    }
}
