namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class DemoFixturesTests
{
    [Theory]
    [InlineData("demo/old-schema.graphql")]
    [InlineData("demo/new-schema.graphql")]
    [InlineData("demo/android/GetCampaign.graphql")]
    [InlineData("demo/android/GetCampaignDetails.graphql")]
    [InlineData("demo/frontend/CampaignDetails.graphql")]
    [InlineData("demo/frontend/CampaignCard.graphql")]
    [InlineData("demo/frontend/GetUser.graphql")]
    public void DemoFile_Exists_And_IsNotEmpty(string relativePath)
    {
        var path = TestPaths.FromRepositoryRoot(relativePath);

        Assert.True(File.Exists(path), $"Missing demo file: {relativePath}");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)), $"Empty demo file: {relativePath}");
    }
}
