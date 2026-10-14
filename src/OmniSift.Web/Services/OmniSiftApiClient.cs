// ============================================================
// OmniSift.Web — API Client Service
// Typed HTTP client for communicating with the OmniSift API
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using OmniSift.Shared;
using OmniSift.Shared.DTOs;

namespace OmniSift.Web.Services;

/// <summary>
/// Strongly-typed HTTP client for the OmniSift API, registered with
/// <c>AddHttpClient</c>. <see cref="AuthHandler"/> adds the bearer token; the
/// tenant is the token's <c>tenant_id</c> claim. All JSON goes through the
/// source-generated <see cref="OmniSiftJsonContext"/> (no reflection).
/// </summary>
public sealed class OmniSiftApiClient(HttpClient httpClient)
{
    private static OmniSiftJsonContext Json => OmniSiftJsonContext.Default;

    // ── Auth ────────────────────────────────────────────────

    /// <summary>Registers a new account and tenant. Returns the account, or an error message.</summary>
    public async Task<(AuthResponse? Account, string? Error)> RegisterAsync(RegisterRequest req)
    {
        using var response = await httpClient
            .PostAsJsonAsync("api/auth/register", req, Json.RegisterRequest)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
            return (null, "An account with that email already exists.");

        if (!response.IsSuccessStatusCode)
            return (null, $"Registration failed ({(int)response.StatusCode}).");

        var account = await response.Content.ReadFromJsonAsync(Json.AuthResponse).ConfigureAwait(false);
        return account is null ? (null, "Empty registration response.") : (account, null);
    }

    /// <summary>Logs in. Returns AuthResponse on success; throws on failure.</summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        using var response = await httpClient
            .PostAsJsonAsync("api/auth/login", req, Json.LoginRequest)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.AuthResponse).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty login response.");
    }

    // ── Data Sources ────────────────────────────────────────

    public async Task<List<DataSourceDto>> GetDataSourcesAsync()
    {
        using var response = await httpClient.GetAsync("api/datasources").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.ListDataSourceDto).ConfigureAwait(false) ?? [];
    }

    public async Task<DataSourceDto?> GetDataSourceAsync(Guid id)
    {
        using var response = await httpClient.GetAsync($"api/datasources/{id}").ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.DataSourceDto).ConfigureAwait(false);
    }

    public async Task<IngestionResponse> UploadFileAsync(Stream fileStream, string fileName, string? sourceType = null)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(sourceType))
            content.Add(new StringContent(sourceType), "sourceType");

        using var response = await httpClient.PostAsync("api/datasources/upload", content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IngestionResponse).ConfigureAwait(false)
            ?? new IngestionResponse { Status = "error", Message = "Failed to parse response." };
    }

    public async Task<IngestionResponse> IngestWebAsync(string url)
    {
        using var response = await httpClient
            .PostAsJsonAsync("api/datasources/web", new WebIngestionRequest { Url = url }, Json.WebIngestionRequest)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IngestionResponse).ConfigureAwait(false)
            ?? new IngestionResponse { Status = "error", Message = "Failed to parse response." };
    }

    public async Task DeleteDataSourceAsync(Guid id)
    {
        using var response = await httpClient.DeleteAsync($"api/datasources/{id}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // ── Agent ───────────────────────────────────────────────

    public async Task<AgentQueryResponse> QueryAgentAsync(AgentQueryRequest queryRequest)
    {
        using var response = await httpClient
            .PostAsJsonAsync("api/agent/query", queryRequest, Json.AgentQueryRequest)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.AgentQueryResponse).ConfigureAwait(false)
            ?? new AgentQueryResponse { Response = "Failed to parse response." };
    }

    /// <summary>
    /// Streams SSE tokens from /api/agent/query/stream.
    /// Yields SseEvent.Delta for each token and SseEvent.Final when done.
    /// </summary>
    public async IAsyncEnumerable<SseEvent> QueryAgentStreamAsync(
        AgentQueryRequest queryRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/agent/query/stream")
        {
            Content = JsonContent.Create(queryRequest, Json.AgentQueryRequest)
        };

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

    /// <summary>
    /// Downloads a PDF report. Returns raw bytes for blob download via JS interop.
    /// </summary>
    public async Task<byte[]> GenerateReportPdfAsync(GenerateReportRequest reportRequest)
    {
        using var response = await httpClient
            .PostAsJsonAsync("api/agent/report/pdf", reportRequest, Json.GenerateReportRequest)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    // ── Entities ────────────────────────────────────────────

    public async Task<EntityGraphResponse> ExtractEntitiesAsync()
    {
        using var response = await httpClient.PostAsync("api/entities/extract", content: null).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.EntityGraphResponse).ConfigureAwait(false)
            ?? new EntityGraphResponse();
    }

    // ── Health ──────────────────────────────────────────────

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            using var response = await httpClient.GetAsync("health").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
