namespace Veel.GraphQL.SchemaIntelligence.Tests;

/// <summary>Records each request (content is read eagerly, before disposal) and returns a configured response.</summary>
internal sealed class FakeHttpMessageHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Content?.Headers.ContentType?.MediaType,
            request.Content?.Headers.ContentType?.CharSet,
            request.Headers.Authorization?.ToString(),
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

        return await respond(cancellationToken);
    }
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Uri,
    string? ContentType,
    string? CharSet,
    string? Authorization,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);
