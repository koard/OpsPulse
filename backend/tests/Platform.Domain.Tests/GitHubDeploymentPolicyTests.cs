using System.Security.Cryptography;
using System.Text;
using Platform.Domain;

namespace Platform.Domain.Tests;

public sealed class GitHubDeploymentPolicyTests
{
    [Fact]
    public void VerifySignature_AcceptsValidSha256Signature()
    {
        var body = """{"zen":"keep it logically awesome"}""";
        var secret = "test-secret";
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        Assert.True(GitHubDeploymentPolicy.VerifySignature(body, signature, secret));
    }

    [Fact]
    public void EvaluatePush_IgnoresNonMainBranch()
    {
        var repository = GitRepository.CreateDefault(
            service: "frontend",
            fullName: "koard/DukeFarm-Frontend",
            branch: "main",
            projectId: "dukefarm");
        var push = new GitHubPushEvent(
            RepositoryFullName: "koard/DukeFarm-Frontend",
            Ref: "refs/heads/dev",
            SenderLogin: "koard",
            HeadCommit: SampleCommit());

        var decision = GitHubDeploymentPolicy.EvaluatePush(repository, push);

        Assert.False(decision.ShouldCreateCommand);
        Assert.Equal("Ignored non-main branch refs/heads/dev.", decision.Reason);
    }

    [Fact]
    public void EvaluatePush_RecordsCommitOnlyWhenAutoDeployIsOff()
    {
        var repository = GitRepository.CreateDefault(
            service: "frontend",
            fullName: "koard/DukeFarm-Frontend",
            branch: "main",
            projectId: "dukefarm") with
        {
            AutoDeployEnabled = false
        };
        var push = new GitHubPushEvent(
            RepositoryFullName: "koard/DukeFarm-Frontend",
            Ref: "refs/heads/main",
            SenderLogin: "koard",
            HeadCommit: SampleCommit());

        var decision = GitHubDeploymentPolicy.EvaluatePush(repository, push);

        Assert.False(decision.ShouldCreateCommand);
        Assert.Equal("Auto deploy is disabled.", decision.Reason);
    }

    [Theory]
    [InlineData("backend", OpsCommandAction.RedeployBackend)]
    [InlineData("frontend", OpsCommandAction.RedeployFrontend)]
    [InlineData("admin", OpsCommandAction.RedeployAdmin)]
    public void EvaluatePush_MapsServiceToRedeployAction(string service, OpsCommandAction expectedAction)
    {
        var repository = GitRepository.CreateDefault(
            service: service,
            fullName: $"koard/DukeFarm-{service}",
            branch: "main",
            projectId: "dukefarm") with
        {
            AutoDeployEnabled = true
        };
        var push = new GitHubPushEvent(
            RepositoryFullName: repository.FullName,
            Ref: "refs/heads/main",
            SenderLogin: "koard",
            HeadCommit: SampleCommit());

        var decision = GitHubDeploymentPolicy.EvaluatePush(repository, push);

        Assert.True(decision.ShouldCreateCommand);
        Assert.Equal(expectedAction, decision.Action);
        Assert.Equal("github:koard", decision.RequestedBy);
    }

    private static GitCommit SampleCommit()
    {
        return new GitCommit(
            RepositoryId: "frontend",
            Sha: "abc123",
            Message: "Update frontend",
            AuthorName: "Ratchanon",
            AuthorLogin: "koard",
            Url: "https://github.com/koard/DukeFarm-Frontend/commit/abc123",
            CommittedAt: DateTimeOffset.Parse("2026-05-13T12:00:00Z"));
    }
}
