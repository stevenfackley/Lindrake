// ============================================================
// OmniSift.Web — API Client Service
// Typed HTTP client for communicating with the OmniSift API
// ============================================================

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using OmniSift.Shared.DTOs;

namespace OmniSift.Web.Services;

/// <summary>
/// Strongly-typed HTTP client for the OmniSift API.
/// Automatically includes the X-Tenant-Id header on all requests.
/// When AuthStateService carries a JWT it is sent as Bearer as well.
/// </summary>
public sealed class OmniSiftApiClient(HttpClient httpClient, AuthStateService auth)
{
    /// <summary>
    /// The current tenant ID. Overridden by AuthStateService when the user is signed in.
    /// </summary>
    public Guid TenantId => auth.IsAuthenticated && auth.TenantId != Guid.Empty
        ? auth.TenantId
        : Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", TenantId.ToString());

        if (auth.IsAuthenticated && !string.IsNullOrEmpty(auth.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        return request;
    }

    // ── Auth ────────────────────────────────────────────────

    /// <summary>Registers a new account. Returns null on success; non-null string = error.</summary>
    public async Task<string?> RegisterAsync(RegisterRequest req)
    {
        var response = await httpClient
            .PostAsJsonAsync("api/auth/register", req)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return "An account with that email already exists.";

        return $"Registration failed ({(int)response.StatusCode}).";
    }

    /// <summary>Logs in. Returns AuthResponse on success; throws on failure.</summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var response = await httpClient
            .PostAsJsonAsync("api/auth/login", req)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthResponse>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty login response.");
    }

    // ── Data Sources ────────────────────────────────────────

    public async Task<List<DataSourceDto>> GetDataSourcesAsync()
    {
        var request = CreateRequest(HttpMethod.Get, "api/datasources");
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<DataSourceDto>>().ConfigureAwait(false) ?? [];
    }

    public async Task<DataSourceDto?> GetDataSourceAsync(Guid id)
    {
        var request = CreateRequest(HttpMethod.Get, $"api/datasources/{id}");
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DataSourceDto>().ConfigureAwait(false);
    }

    public async Task<IngestionResponse> UploadFileAsync(Stream fileStream, string fileName, string? sourceType = null)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(sourceType))
            content.Add(new StringContent(sourceType), "sourceType");

        var request = new HttpRequestMessage(HttpMethod.Post, "api/datasources/upload")
        {
            Content = content
        };
        request.Headers.Add("X-Tenant-Id", TenantId.ToString());
        if (auth.IsAuthenticated && !string.IsNullOrEmpty(auth.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IngestionResponse>().ConfigureAwait(false)
            ?? new IngestionResponse { Status = "error", Message = "Failed to parse response." };
    }

    public async Task<IngestionResponse> IngestWebAsync(string url)
    {
        var request = CreateRequest(HttpMethod.Post, "api/datasources/web");
        request.Content = JsonContent.Create(new WebIngestionRequest { Url = url });
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IngestionResponse>().ConfigureAwait(false)
            ?? new IngestionResponse { Status = "error", Message = "Failed to parse response." };
    }

    public async Task DeleteDataSourceAsync(Guid id)
    {
        var request = CreateRequest(HttpMethod.Delete, $"api/datasources/{id}");
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // ── Agent ───────────────────────────────────────────────

    public async Task<AgentQueryResponse> QueryAgentAsync(AgentQueryRequest queryRequest)
    {
        var request = CreateRequest(HttpMethod.Post, "api/agent/query");
        request.Content = JsonContent.Create(queryRequest);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentQueryResponse>().ConfigureAwait(false)
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
        var request = CreateRequest(HttpMethod.Post, "api/agent/query/stream");
        request.Content = JsonContent.Create(queryRequest);

        // Blazor WASM requires this to enable streaming response bodies
        request.SetBrowserResponseStreamingEnabled(true);

        var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

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
        var request = CreateRequest(HttpMethod.Post, "api/agent/report/pdf");
        request.Content = JsonContent.Create(reportRequest);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    // ── Entities ────────────────────────────────────────────

    public async Task<EntityGraphResponse> ExtractEntitiesAsync()
    {
        var request = CreateRequest(HttpMethod.Post, "api/entities/extract");
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EntityGraphResponse>().ConfigureAwait(false)
            ?? new EntityGraphResponse();
    }

    // ── Health ──────────────────────────────────────────────

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("health").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
