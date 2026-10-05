using Veel.GraphQL.SchemaIntelligence.Notifications;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class WebhookOptionsTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    private static string WriteConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"schema-intelligence-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Defaults_AreDisabledWithTenSecondTimeout()
    {
        var options = WebhookOptions.Load(null, Environment());

        Assert.False(options.Enabled);
        Assert.Null(options.Url);
        Assert.Null(options.BearerToken);
        Assert.Equal(10, options.TimeoutSeconds);
    }

    [Fact]
    public void ConfigFile_IsRead()
    {
        var path = WriteConfig("""
            {
              // comments are allowed
              "SchemaIntelligence": {
                "Webhook": { "Enabled": true, "Url": "https://example.test/hook", "TimeoutSeconds": 5 }
              }
            }
            """);
        try
        {
            var options = WebhookOptions.Load(path, Environment());

            Assert.True(options.Enabled);
            Assert.Equal("https://example.test/hook", options.Url);
            Assert.Equal(5, options.TimeoutSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnvironmentVariables_OverrideConfigFile()
    {
        var path = WriteConfig("""{ "SchemaIntelligence": { "Webhook": { "Enabled": false, "Url": "https://file.test" } } }""");
        try
        {
            var options = WebhookOptions.Load(path, Environment(
                (WebhookOptions.EnabledVariable, "true"),
                (WebhookOptions.UrlVariable, "https://env.test/hook"),
                (WebhookOptions.TimeoutSecondsVariable, "3"),
                (WebhookOptions.BearerTokenVariable, "token-from-env")));

            Assert.True(options.Enabled);
            Assert.Equal("https://env.test/hook", options.Url);
            Assert.Equal(3, options.TimeoutSeconds);
            Assert.Equal("token-from-env", options.BearerToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyUrlVariable_IsUsedWhenPrimaryIsMissing()
    {
        var options = WebhookOptions.Load(null, Environment((WebhookOptions.LegacyUrlVariable, "https://legacy.test/hook")));

        Assert.Equal("https://legacy.test/hook", options.Url);
        Assert.False(options.Enabled);
    }

    [Theory]
    [InlineData(WebhookOptions.EnabledVariable, "yes", "must be 'true' or 'false'")]
    [InlineData(WebhookOptions.TimeoutSecondsVariable, "ten", "must be a whole number")]
    public void InvalidEnvironmentValues_FailClearly(string variable, string value, string expected)
    {
        var ex = Assert.Throws<NotificationException>(() => WebhookOptions.Load(null, Environment((variable, value))));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void MissingConfigFile_FailsClearly()
    {
        var ex = Assert.Throws<NotificationException>(() => WebhookOptions.Load("does-not-exist.json", Environment()));

        Assert.Equal("Configuration file does not exist: does-not-exist.json", ex.Message);
    }

    [Fact]
    public void InvalidConfigFile_FailsWithoutQuotingContent()
    {
        var path = WriteConfig("""{ "SchemaIntelligence": { "Webhook": { "BearerToken": "leaky-token" """);
        try
        {
            var ex = Assert.Throws<NotificationException>(() => WebhookOptions.Load(path, Environment()));

            Assert.StartsWith("Configuration file is not valid JSON", ex.Message);
            Assert.DoesNotContain("leaky-token", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
