namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class DemoFixturesTests
{
    [Theory]
    [InlineData($"{TestPaths.Fixtures}/old-schema.graphql")]
    [InlineData($"{TestPaths.Fixtures}/new-schema.graphql")]
    [InlineData($"{TestPaths.Fixtures}/android/GetCampaign.graphql")]
    [InlineData($"{TestPaths.Fixtures}/android/GetCampaignDetails.graphql")]
    [InlineData($"{TestPaths.Fixtures}/frontend/CampaignDetails.graphql")]
    [InlineData($"{TestPaths.Fixtures}/frontend/CampaignCard.graphql")]
    [InlineData($"{TestPaths.Fixtures}/frontend/GetUser.graphql")]
    public void DemoFile_Exists_And_IsNotEmpty(string relativePath)
    {
        var path = TestPaths.FromRepositoryRoot(relativePath);

        Assert.True(File.Exists(path), $"Missing demo file: {relativePath}");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)), $"Empty demo file: {relativePath}");
    }
}
