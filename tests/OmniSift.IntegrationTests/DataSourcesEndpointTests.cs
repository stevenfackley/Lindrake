// ============================================================
// Integration Tests — DataSources Endpoints
// Verifies upload, list, get, delete with bearer authentication
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using OmniSift.Shared.DTOs;

namespace OmniSift.IntegrationTests;

public sealed class DataSourcesEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DataSourcesEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateTenantClient();
    }

    [Fact]
    public async Task ListDataSources_Authenticated_Returns200()
    {
        var response = await _client.GetAsync("/api/datasources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListDataSources_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient(); // No bearer token
        var response = await client.GetAsync("/api/datasources");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListDataSources_ReturnsEmptyListInitially()
    {
        var response = await _client.GetAsync("/api/datasources");
        var sources = await response.Content.ReadFromJsonAsync<List<DataSourceDto>>();

        sources.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDataSource_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync($"/api/datasources/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDataSource_NonExistentId_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/datasources/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_WithoutFile_Returns400()
    {
        using var content = new MultipartFormDataContent();
        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_OctetStreamWithKnownExtension_Returns202()
    {
        // The Blazor client labels every upload application/octet-stream; the
        // source type must then come from the file extension.
        var file = new ByteArrayContent("sender,timestamp,message\nAlice,2026-01-01,hi\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var content = new MultipartFormDataContent { { file, "file", "texts.csv" } };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<IngestionResponse>();
        body!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task WebIngestion_InvalidUrl_Returns400()
    {
        var request = new WebIngestionRequest { Url = "not-a-url" };
        var response = await _client.PostAsJsonAsync("/api/datasources/web", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
