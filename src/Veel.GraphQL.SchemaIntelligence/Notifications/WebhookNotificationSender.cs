using System.Net.Http.Headers;
using System.Text;
using Veel.GraphQL.SchemaIntelligence.Reporting;

namespace Veel.GraphQL.SchemaIntelligence.Notifications;

/// <summary>
/// POSTs the report to a generic HTTP endpoint. Provider-agnostic: the body is exactly the JSON report
/// (<see cref="JsonReportGenerator"/>), so any receiver that understands that contract can consume it.
/// </summary>
/// <remarks>
/// Event metadata travels in headers rather than a wrapper object, so the body stays identical to the JSON report:
/// <c>X-Schema-Intelligence-Event: graphql.schema.changed</c> and <c>X-Schema-Intelligence-Report-Version</c>.
/// No retries: a failure surfaces immediately as a <see cref="NotificationException"/>.
/// </remarks>
public sealed class WebhookNotificationSender(HttpClient httpClient, WebhookOptions options) : INotificationSender
{
    public const string EventType = "graphql.schema.changed";
    public const string EventHeader = "X-Schema-Intelligence-Event";
    public const string ReportVersionHeader = "X-Schema-Intelligence-Report-Version";

    private readonly JsonReportGenerator _jsonGenerator = new();

    public async Task SendAsync(ChangeReport report, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return;
        }

        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Url)
        {
            Content = new StringContent(_jsonGenerator.Generate(report), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(EventHeader, EventType);
        request.Headers.Add(ReportVersionHeader, JsonReportGenerator.ReportVersion.ToString());
        if (!string.IsNullOrEmpty(options.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);
        }

        // The timeout is applied per request, so a shared HttpClient's own Timeout is left untouched.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the caller's cancellation: either our timeout, or the HttpClient's own Timeout fired first.
            throw new NotificationException(timeout.IsCancellationRequested
                ? $"Webhook notification timed out after {options.TimeoutSeconds} second(s)."
                : "Webhook notification timed out (HTTP client timeout).");
        }
        catch (HttpRequestException ex)
        {
            // The inner exception is kept for debugging; the message stays free of the URL.
            throw new NotificationException("Webhook notification failed: the webhook endpoint could not be reached.", ex);
        }

        using (response)
        {
            // The response body is deliberately not read or included: it may echo sensitive data.
            if (!response.IsSuccessStatusCode)
            {
                throw new NotificationException(
                    $"Webhook notification failed with HTTP status {(int)response.StatusCode} ({response.StatusCode}).");
            }
        }
    }
}
