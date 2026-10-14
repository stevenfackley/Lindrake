// ============================================================
// OmniSift.Web — API Client Service
// Typed HTTP client for communicating with the OmniSift API
// ============================================================

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using OmniSift.Shared;
using OmniSift.Shared.DTOs;

namespace OmniSift.Web.Services;

/// <summary>
/// Strongly-typed HTTP client for the OmniSift API, registered with
/// <c>AddHttpClient</c>. <see cref="AuthHandler"/> adds the bearer token; the
/// tenant is the token's <c>tenant_id</c> claim. All JSON goes through the
/// source-generated <see cref="OmniSiftJsonContext"/> (no reflection).
/// <para>
/// Every call returns an <see cref="ApiResult"/> instead of throwing: a non-2xx
/// response becomes an <see cref="ApiError"/> carrying the ProblemDetails message,
/// and a 401 while signed in signs the user out (routing then sends them to the
/// login page). Only cancellation of the caller's token throws.
/// </para>
/// </summary>
public sealed class OmniSiftApiClient(HttpClient httpClient, AuthStateService auth)
{
    private static OmniSiftJsonContext Json => OmniSiftJsonContext.Default;

    // ── Auth ────────────────────────────────────────────────

    /// <summary>Registers a new account and tenant.</summary>
    public Task<ApiResult<AuthResponse>> RegisterAsync(RegisterRequest req, CancellationToken cancellationToken = default) =>
        SendAsync(PostJson("api/auth/register", req, Json.RegisterRequest), Json.AuthResponse, cancellationToken);

    /// <summary>Exchanges credentials for a token. Bad credentials are a 401 error.</summary>
    public Task<ApiResult<AuthResponse>> LoginAsync(LoginRequest req, CancellationToken cancellationToken = default) =>
        SendAsync(PostJson("api/auth/login", req, Json.LoginRequest), Json.AuthResponse, cancellationToken);

    // ── Data Sources ────────────────────────────────────────

    public Task<ApiResult<List<DataSourceDto>>> GetDataSourcesAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/datasources"), Json.ListDataSourceDto, cancellationToken);

    public Task<ApiResult<DataSourceDto>> GetDataSourceAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/datasources/{id}"), Json.DataSourceDto, cancellationToken);

    public Task<ApiResult<IngestionResponse>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(sourceType))
            content.Add(new StringContent(sourceType), "sourceType");

        var request = new HttpRequestMessage(HttpMethod.Post, "api/datasources/upload") { Content = content };
        return SendAsync(request, Json.IngestionResponse, cancellationToken);
    }

    public Task<ApiResult<IngestionResponse>> IngestWebAsync(string url, CancellationToken cancellationToken = default) =>
        SendAsync(
            PostJson("api/datasources/web", new WebIngestionRequest { Url = url }, Json.WebIngestionRequest),
            Json.IngestionResponse,
            cancellationToken);

    public Task<ApiResult> DeleteDataSourceAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/datasources/{id}"), cancellationToken);

    // ── Agent ───────────────────────────────────────────────

    public Task<ApiResult<AgentQueryResponse>> QueryAgentAsync(
        AgentQueryRequest queryRequest,
        CancellationToken cancellationToken = default) =>
        SendAsync(PostJson("api/agent/query", queryRequest, Json.AgentQueryRequest), Json.AgentQueryResponse, cancellationToken);

    /// <summary>
    /// Streams SSE tokens from /api/agent/query/stream.
    /// Yields SseEvent.Delta for each token and SseEvent.Final when done.
    /// </summary>
    public async IAsyncEnumerable<SseEvent> QueryAgentStreamAsync(
        AgentQueryRequest queryRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = PostJson("api/agent/query/stream", queryRequest, Json.AgentQueryRequest);

        // Blazor WASM requires this to enable streaming response bodies
        request.SetBrowserResponseStreamingEnabled(true);

        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;

            var evt = SseParser.ParseLine(line);
            if (evt is not null)
                yield return evt;
        }
    }

    // ── Report ──────────────────────────────────────────────

    /// <summary>Generates a PDF report; the bytes are handed to JS interop for download.</summary>
    public Task<ApiResult<byte[]>> GenerateReportPdfAsync(
        GenerateReportRequest reportRequest,
        CancellationToken cancellationToken = default) =>
        SendAsync<byte[]>(
            PostJson("api/agent/report/pdf", reportRequest, Json.GenerateReportRequest),
            async (content, token) => await content.ReadAsByteArrayAsync(token).ConfigureAwait(false),
            cancellationToken);

    // ── Entities ────────────────────────────────────────────

    public Task<ApiResult<EntityGraphResponse>> ExtractEntitiesAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, "api/entities/extract"), Json.EntityGraphResponse, cancellationToken);

    // ── Health ──────────────────────────────────────────────

    /// <summary>True when GET /health answers 2xx.</summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "health"), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess;
    }

    // ── Plumbing ────────────────────────────────────────────

    private static HttpRequestMessage PostJson<TBody>(string url, TBody body, JsonTypeInfo<TBody> bodyType) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body, bodyType) };

    private Task<ApiResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken) =>
        SendAsync(request, (content, token) => content.ReadFromJsonAsync(responseType, token), cancellationToken);

    /// <summary>The single request path: send, map failures to <see cref="ApiError"/>, read the body.</summary>
    private async Task<ApiResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        Func<HttpContent, CancellationToken, Task<T?>> readBody,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            var (response, error) = await TrySendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            if (response is null)
                return ApiResult<T>.Failure(error!);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    return ApiResult<T>.Failure(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));

                try
                {
                    var value = await readBody(response.Content, cancellationToken).ConfigureAwait(false);
                    return value is null
                        ? ApiResult<T>.Failure(ApiError.InvalidResponse((int)response.StatusCode))
                        : ApiResult<T>.Success(value);
                }
                catch (JsonException)
                {
                    return ApiResult<T>.Failure(ApiError.InvalidResponse((int)response.StatusCode));
                }
            }
        }
    }

    private async Task<ApiResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            var (response, error) = await TrySendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            if (response is null)
                return ApiResult.Failure(error!);

            using (response)
            {
                return response.IsSuccessStatusCode
                    ? ApiResult.Success()
                    : ApiResult.Failure(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
            }
        }
    }

    /// <summary>Sends <paramref name="request"/>; a transport failure or timeout becomes an <see cref="ApiError"/>.</summary>
    private async Task<(HttpResponseMessage? Response, ApiError? Error)> TrySendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await httpClient.SendAsync(request, completion, cancellationToken).ConfigureAwait(false), null);
        }
        catch (HttpRequestException)
        {
            return (null, ApiError.Unreachable());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout, not the caller cancelling.
            return (null, ApiError.TimedOut());
        }
    }

    /// <summary>Reads the ProblemDetails body; a 401 while signed in ends the session.</summary>
    private async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var error = await ApiProblemReader.ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (error.IsUnauthorized && auth.IsAuthenticated)
            await auth.SignOutAsync().ConfigureAwait(false);

        return error;
    }
}
