// ============================================================
// Unit Tests — scripted HttpMessageHandler for the Blazor client
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace OmniSift.UnitTests.TestSupport;

/// <summary>What a request looked like when it reached the handler (the client disposes it afterwards).</summary>
internal sealed record CapturedRequest(
    HttpMethod Method,
    string PathAndQuery,
    AuthenticationHeaderValue? Authorization,
    string? Body,
    string? ContentType,
    IReadOnlyList<string> HeaderNames);

/// <summary>
/// Answers each request with the next scripted response (the last one repeats)
/// and records what was sent.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly List<CapturedRequest> _requests = [];

    public FakeHttpMessageHandler(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    public FakeHttpMessageHandler(Func<CapturedRequest, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_requests)
                return [.. _requests];
        }
    }

    /// <summary>Responds with <paramref name="responses"/> in order, repeating the last.</summary>
    public static FakeHttpMessageHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var next = 0;
        return new FakeHttpMessageHandler(_ =>
        {
            var index = Math.Min(Interlocked.Increment(ref next) - 1, responses.Length - 1);
            return responses[index]();
        });
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string? detail = null, string? title = null)
    {
        var fields = new List<string> { $"\"status\":{(int)status}" };
        if (title is not null)
            fields.Add($"\"title\":\"{title}\"");
        if (detail is not null)
            fields.Add($"\"detail\":\"{detail}\"");
        return Json(status, "{" + string.Join(",", fields) + "}", "application/problem+json");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization,
            body,
            request.Content?.Headers.ContentType?.MediaType,
            [.. request.Headers.Select(h => h.Key)]);

        lock (_requests)
            _requests.Add(captured);

        cancellationToken.ThrowIfCancellationRequested();
        return await _respond(captured, cancellationToken);
    }
}
