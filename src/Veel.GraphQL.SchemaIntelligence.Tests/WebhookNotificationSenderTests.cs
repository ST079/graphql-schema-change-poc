using System.Net;
using System.Text.Json;
using Veel.GraphQL.SchemaIntelligence.Notifications;
using Veel.GraphQL.SchemaIntelligence.Reporting;

namespace Veel.GraphQL.SchemaIntelligence.Tests;

public class WebhookNotificationSenderTests
{
    // A URL with a token in its path, like many real webhook URLs, to check it never leaks into messages.
    private const string WebhookUrl = "https://hooks.example.test/notify/secret-path-token";
    private const string BearerToken = "test-bearer-token-value";

    private static WebhookOptions EnabledOptions(string? token = null, int timeoutSeconds = 10) =>
        new() { Enabled = true, Url = WebhookUrl, BearerToken = token, TimeoutSeconds = timeoutSeconds };

    private static (WebhookNotificationSender Sender, FakeHttpMessageHandler Handler) CreateSender(
        WebhookOptions options, Func<CancellationToken, Task<HttpResponseMessage>>? respond = null)
    {
        var handler = new FakeHttpMessageHandler(respond ?? (_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        return (new WebhookNotificationSender(new HttpClient(handler), options), handler);
    }

    private static async Task<JsonElement> SendAndCaptureBody(ChangeReport report)
    {
        var (sender, handler) = CreateSender(EnabledOptions());
        await sender.SendAsync(report);
        return JsonDocument.Parse(Assert.Single(handler.Requests).Body!).RootElement.Clone();
    }

    // ---- Disabled and misconfigured ------------------------------------------------------------

    [Fact]
    public async Task Disabled_MakesNoRequestAndSucceeds()
    {
        var (sender, handler) = CreateSender(new WebhookOptions { Enabled = false, Url = WebhookUrl });

        await sender.SendAsync(TestReports.DemoLike());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DisabledByDefault()
    {
        var (sender, handler) = CreateSender(new WebhookOptions());

        await sender.SendAsync(TestReports.DemoLike());

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null, "no webhook URL is configured")]
    [InlineData("", "no webhook URL is configured")]
    [InlineData("/relative/path", "not a valid absolute http(s) URL")]
    [InlineData("ftp://example.test/hook", "not a valid absolute http(s) URL")]
    public async Task EnabledWithInvalidUrl_FailsClearlyWithoutRequest(string? url, string expected)
    {
        var (sender, handler) = CreateSender(new WebhookOptions { Enabled = true, Url = url });

        var ex = await Assert.ThrowsAsync<NotificationException>(() => sender.SendAsync(TestReports.DemoLike()));

        Assert.Contains(expected, ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NonPositiveTimeout_FailsClearly()
    {
        var (sender, _) = CreateSender(EnabledOptions(timeoutSeconds: 0));

        var ex = await Assert.ThrowsAsync<NotificationException>(() => sender.SendAsync(TestReports.DemoLike()));

        Assert.Contains("timeout must be a positive", ex.Message);
    }

    // ---- Request shape ----------------------------------------------------------------------

    [Fact]
    public async Task Send_PostsJsonToConfiguredUrl()
    {
        var (sender, handler) = CreateSender(EnabledOptions());

        await sender.SendAsync(TestReports.DemoLike());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri(WebhookUrl), request.Uri);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("utf-8", request.CharSet);
    }

    [Fact]
    public async Task Send_IncludesEventMetadataHeaders()
    {
        var (sender, handler) = CreateSender(EnabledOptions());

        await sender.SendAsync(TestReports.DemoLike());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("graphql.schema.changed", request.Headers[WebhookNotificationSender.EventHeader]);
        Assert.Equal("1", request.Headers[WebhookNotificationSender.ReportVersionHeader]);
    }

    [Fact]
    public async Task BearerToken_IsSentOnlyWhenConfigured()
    {
        var (withToken, withTokenHandler) = CreateSender(EnabledOptions(token: BearerToken));
        var (withoutToken, withoutTokenHandler) = CreateSender(EnabledOptions());

        await withToken.SendAsync(TestReports.DemoLike());
        await withoutToken.SendAsync(TestReports.DemoLike());

        Assert.Equal($"Bearer {BearerToken}", withTokenHandler.Requests[0].Authorization);
        Assert.Null(withoutTokenHandler.Requests[0].Authorization);
        Assert.DoesNotContain(BearerToken, withTokenHandler.Requests[0].Body);
    }

    // ---- Payload ----------------------------------------------------------------------------

    [Fact]
    public async Task Payload_IsExactlyTheJsonReport()
    {
        var report = TestReports.DemoLike();
        var (sender, handler) = CreateSender(EnabledOptions());

        await sender.SendAsync(report);

        Assert.Equal(new JsonReportGenerator().Generate(report), handler.Requests[0].Body);
    }

    [Fact]
    public async Task Payload_ContainsServiceSummaryChangesAndClientImpacts()
    {
        var body = await SendAndCaptureBody(TestReports.DemoLike());

        Assert.Equal("Cerberus", body.GetProperty("serviceName").GetString());

        var summary = body.GetProperty("summary");
        Assert.Equal(
            (3, 1, 0, 2, 2, 3),
            (summary.GetProperty("totalChanges").GetInt32(), summary.GetProperty("breakingChanges").GetInt32(),
             summary.GetProperty("warningChanges").GetInt32(), summary.GetProperty("infoChanges").GetInt32(),
             summary.GetProperty("affectedClients").GetInt32(), summary.GetProperty("affectedOperations").GetInt32()));

        var removed = body.GetProperty("changes")[0];
        Assert.Equal("FieldRemoved", removed.GetProperty("changeType").GetString());
        Assert.Equal("Breaking", removed.GetProperty("severity").GetString());
        Assert.Equal("Campaign", removed.GetProperty("schemaType").GetString());
        Assert.Equal("videoUrl", removed.GetProperty("fieldName").GetString());
        Assert.Equal("String", removed.GetProperty("oldType").GetString());
        Assert.False(string.IsNullOrEmpty(removed.GetProperty("reason").GetString()));
        Assert.False(string.IsNullOrEmpty(removed.GetProperty("recommendedAction").GetString()));

        var android = body.GetProperty("clientImpacts")[0];
        Assert.Equal("Android", android.GetProperty("clientName").GetString());
        Assert.Equal("Breaking", android.GetProperty("severity").GetString());
        Assert.Equal(
            ["GetCampaign", "GetCampaignDetails"],
            android.GetProperty("affectedOperations").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal(
            ["CampaignDetails"],
            body.GetProperty("clientImpacts")[1].GetProperty("affectedOperations").EnumerateArray().Select(o => o.GetString()));
    }

    [Fact]
    public async Task Payload_SerializesEnumsAsNames()
    {
        var (sender, handler) = CreateSender(EnabledOptions());

        await sender.SendAsync(TestReports.DemoLike());

        Assert.Contains("\"severity\": \"Breaking\"", handler.Requests[0].Body);
        Assert.Contains("\"changeType\": \"FieldRemoved\"", handler.Requests[0].Body);
        Assert.DoesNotContain("\"severity\": 2", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Payload_PreservesNulls()
    {
        var removed = (await SendAndCaptureBody(TestReports.DemoLike())).GetProperty("changes")[0];

        Assert.Equal("FieldRemoved", removed.GetProperty("changeType").GetString());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("newType").ValueKind);
    }

    // ---- Responses ----------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task SuccessStatus_Completes(HttpStatusCode status)
    {
        var (sender, handler) = CreateSender(EnabledOptions(), _ => Task.FromResult(new HttpResponseMessage(status)));

        await sender.SendAsync(TestReports.DemoLike());

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FailureStatus_ThrowsClearErrorWithoutSecretsOrBody(HttpStatusCode status)
    {
        var (sender, _) = CreateSender(
            EnabledOptions(token: BearerToken),
            _ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("server echoed secret-path-token") }));

        var ex = await Assert.ThrowsAsync<NotificationException>(() => sender.SendAsync(TestReports.DemoLike()));

        Assert.Equal($"Webhook notification failed with HTTP status {(int)status} ({status}).", ex.Message);
        AssertNoSecrets(ex);
    }

    [Fact]
    public async Task UnreachableEndpoint_ThrowsClearErrorWithoutSecrets()
    {
        var (sender, _) = CreateSender(
            EnabledOptions(token: BearerToken),
            _ => throw new HttpRequestException($"Connection refused ({WebhookUrl})"));

        var ex = await Assert.ThrowsAsync<NotificationException>(() => sender.SendAsync(TestReports.DemoLike()));

        Assert.Equal("Webhook notification failed: the webhook endpoint could not be reached.", ex.Message);
        AssertNoSecrets(ex);
    }

    // ---- Cancellation and timeout -----------------------------------------------------------------

    [Fact]
    public async Task AlreadyCancelledToken_IsRespectedWithoutRequest()
    {
        var (sender, handler) = CreateSender(EnabledOptions());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(TestReports.DemoLike(), new CancellationToken(canceled: true)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancellationDuringRequest_IsRespected()
    {
        using var cancellation = new CancellationTokenSource();
        var (sender, _) = CreateSender(EnabledOptions(), async ct =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(TestReports.DemoLike(), cancellation.Token));
    }

    [Fact]
    public async Task SlowEndpoint_TimesOutWithClearError()
    {
        var (sender, _) = CreateSender(EnabledOptions(timeoutSeconds: 1), async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var ex = await Assert.ThrowsAsync<NotificationException>(() => sender.SendAsync(TestReports.DemoLike()));

        Assert.Equal("Webhook notification timed out after 1 second(s).", ex.Message);
    }

    [Fact]
    public async Task HttpClientTimeout_IsNotReportedAsConfiguredTimeout()
    {
        // Regression: when the HttpClient's own Timeout fired first, the error claimed the configured 30 s had elapsed.
        var handler = new FakeHttpMessageHandler(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sender = new WebhookNotificationSender(
            new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) },
            EnabledOptions(timeoutSeconds: 30));

        var ex = await Assert.ThrowsAsync<NotificationException>(() => sender.SendAsync(TestReports.DemoLike()));

        Assert.Equal("Webhook notification timed out (HTTP client timeout).", ex.Message);
    }

    private static void AssertNoSecrets(Exception ex)
    {
        Assert.DoesNotContain("secret-path-token", ex.Message);
        Assert.DoesNotContain(BearerToken, ex.Message);
        Assert.DoesNotContain("hooks.example.test", ex.Message);
    }
}
