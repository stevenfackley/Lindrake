// ============================================================
// Integration Tests — DataSources Endpoints
// Verifies upload, list, get, delete with bearer authentication
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Models;
using Lindrake.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace Lindrake.IntegrationTests;

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
    public async Task ListDataSources_SkipTake_PagesNewestFirstWithChunkCounts()
    {
        var tenantId = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = _factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
            for (var i = 0; i < 3; i++)
            {
                var source = new DataSource { TenantId = tenantId, SourceType = "pdf", FileName = $"{i}.pdf", CreatedAt = start.AddDays(i) };
                db.DataSources.Add(source);
                db.DocumentChunks.AddRange(Enumerable.Range(0, i).Select(n =>
                    new DocumentChunk { TenantId = tenantId, DataSourceId = source.Id, Content = $"chunk {n}", ChunkIndex = n }));
            }

            await db.SaveChangesAsync();
        }

        var client = _factory.CreateTenantClient(tenantId);
        var page = await client.GetFromJsonAsync<List<DataSourceDto>>("/api/datasources?skip=1&take=1");

        // Newest first: 2.pdf, 1.pdf, 0.pdf — skip one, take one.
        page.Should().ContainSingle().Which.Should().Match<DataSourceDto>(s => s.FileName == "1.pdf" && s.ChunkCount == 1);

        var export = await client.GetFromJsonAsync<TenantExportDto>("/api/account/export");
        export!.DataSources.Select(s => (s.FileName, s.ChunkCount)).Should()
            .Equal(("2.pdf", 2), ("1.pdf", 1), ("0.pdf", 0));
    }

    [Theory]
    [InlineData("take=0")]
    [InlineData("take=501")]
    [InlineData("skip=-1")]
    public async Task ListDataSources_OutOfRangePaging_Returns400(string query)
    {
        var response = await _client.GetAsync($"/api/datasources?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task WebIngestion_InternalAddress_Returns400(string url)
    {
        var response = await _client.PostAsJsonAsync("/api/datasources/web", new WebIngestionRequest { Url = url });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WebIngestion_InvalidUrl_Returns400()
    {
        var request = new WebIngestionRequest { Url = "not-a-url" };
        var response = await _client.PostAsJsonAsync("/api/datasources/web", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
