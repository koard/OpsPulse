using System.Security.Cryptography;
using System.Text;

namespace Platform.Domain;

public static class GitHubDeploymentPolicy
{
    public static bool VerifySignature(string payload, string? signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var expected = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signatureHeader);

        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    public static GitHubDeploymentDecision EvaluatePush(
        GitRepository repository,
        GitHubPushEvent push)
    {
        var expectedRef = $"refs/heads/{repository.Branch}";
        if (!string.Equals(push.Ref, expectedRef, StringComparison.OrdinalIgnoreCase))
        {
            return new GitHubDeploymentDecision(
                ShouldCreateCommand: false,
                Action: null,
                RequestedBy: RequestedBy(push.SenderLogin),
                Reason: $"Ignored non-main branch {push.Ref}.");
        }

        if (!repository.AutoDeployEnabled)
        {
            return new GitHubDeploymentDecision(
                ShouldCreateCommand: false,
                Action: null,
                RequestedBy: RequestedBy(push.SenderLogin),
                Reason: "Auto deploy is disabled.");
        }

        return new GitHubDeploymentDecision(
            ShouldCreateCommand: true,
            Action: repository.DeployAction,
            RequestedBy: RequestedBy(push.SenderLogin),
            Reason: "Auto deploy enabled.");
    }

    private static string RequestedBy(string senderLogin)
    {
        return string.IsNullOrWhiteSpace(senderLogin) ? "github:unknown" : $"github:{senderLogin}";
    }
}
