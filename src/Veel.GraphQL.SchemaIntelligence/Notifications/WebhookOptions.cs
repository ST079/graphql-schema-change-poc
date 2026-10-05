using System.Text.Json;

namespace Veel.GraphQL.SchemaIntelligence.Notifications;

/// <summary>
/// Settings for <see cref="WebhookNotificationSender"/>. Disabled by default.
/// Real URLs and tokens belong in environment variables or an untracked config file, never in source control.
/// </summary>
public sealed class WebhookOptions
{
    public const string EnabledVariable = "SCHEMA_INTELLIGENCE_WEBHOOK_ENABLED";
    public const string UrlVariable = "SCHEMA_INTELLIGENCE_WEBHOOK_URL";
    public const string TimeoutSecondsVariable = "SCHEMA_INTELLIGENCE_WEBHOOK_TIMEOUT_SECONDS";
    public const string BearerTokenVariable = "SCHEMA_INTELLIGENCE_WEBHOOK_BEARER_TOKEN";

    /// <summary>Alternative URL variable named in CLAUDE.md; used when <see cref="UrlVariable"/> is not set.</summary>
    public const string LegacyUrlVariable = "GRAPHQL_CHANGE_WEBHOOK_URL";

    public bool Enabled { get; set; }

    /// <summary>Absolute http(s) endpoint. Treated as a secret: many webhook URLs embed a token.</summary>
    public string? Url { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Optional; sent as <c>Authorization: Bearer &lt;token&gt;</c>. Never logged or reported.</summary>
    public string? BearerToken { get; set; }

    /// <summary>
    /// Builds options from an optional JSON config file, then applies environment variables on top.
    /// </summary>
    /// <param name="configFilePath">
    /// File shaped as <c>{ "SchemaIntelligence": { "Webhook": { "Enabled", "Url", "TimeoutSeconds", "BearerToken" } } }</c>.
    /// </param>
    /// <param name="getEnvironmentVariable">Usually <see cref="Environment.GetEnvironmentVariable(string)"/>; injectable for tests.</param>
    /// <exception cref="NotificationException">The file or a variable cannot be read or has an invalid value.</exception>
    public static WebhookOptions Load(string? configFilePath, Func<string, string?> getEnvironmentVariable)
    {
        var options = configFilePath is null ? new WebhookOptions() : FromConfigFile(configFilePath);

        if (getEnvironmentVariable(EnabledVariable) is { Length: > 0 } enabled)
        {
            options.Enabled = enabled.Trim().ToLowerInvariant() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => throw new NotificationException($"{EnabledVariable} must be 'true' or 'false'."),
            };
        }

        if ((getEnvironmentVariable(UrlVariable) ?? getEnvironmentVariable(LegacyUrlVariable)) is { Length: > 0 } url)
        {
            options.Url = url;
        }

        if (getEnvironmentVariable(TimeoutSecondsVariable) is { Length: > 0 } timeout)
        {
            options.TimeoutSeconds = int.TryParse(timeout, out var seconds)
                ? seconds
                : throw new NotificationException($"{TimeoutSecondsVariable} must be a whole number of seconds.");
        }

        if (getEnvironmentVariable(BearerTokenVariable) is { Length: > 0 } token)
        {
            options.BearerToken = token;
        }

        return options;
    }

    /// <summary>Checks the settings needed to send. Messages never include the URL or token.</summary>
    /// <exception cref="NotificationException">The options cannot be used to send a webhook.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new NotificationException("Webhook notification is enabled but no webhook URL is configured.");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new NotificationException("The configured webhook URL is not a valid absolute http(s) URL.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new NotificationException("Webhook timeout must be a positive number of seconds.");
        }
    }

    private static WebhookOptions FromConfigFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new NotificationException($"Configuration file does not exist: {path}");
        }

        try
        {
            var file = JsonSerializer.Deserialize<ConfigFile>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });
            return file?.SchemaIntelligence?.Webhook ?? new WebhookOptions();
        }
        catch (JsonException ex)
        {
            // The parser's message can quote file content (possibly a token), so only the position is reported.
            throw new NotificationException($"Configuration file is not valid JSON: {path} (line {ex.LineNumber + 1}).");
        }
    }

    private sealed class ConfigFile
    {
        public ConfigSection? SchemaIntelligence { get; set; }
    }

    private sealed class ConfigSection
    {
        public WebhookOptions? Webhook { get; set; }
    }
}
