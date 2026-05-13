using Platform.Domain;

namespace Platform.Domain.Tests;

public sealed class ProjectPresentationTests
{
    [Theory]
    [InlineData("dukefarm", "Dukefarm")]
    public void ToDisplayName_RemovesProductionSuffix(string projectId, string expected)
    {
        Assert.Equal(expected, ProjectPresentation.ToDisplayName(projectId));
    }

    [Theory]
    [InlineData("dukefarm")]
    public void ToEnvironment_DoesNotExposeProductionLabel(string projectId)
    {
        Assert.Equal("Connected", ProjectPresentation.ToEnvironment(projectId));
    }
}
