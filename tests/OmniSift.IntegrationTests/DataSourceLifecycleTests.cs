// ============================================================
// Integration Tests — Data source lifecycle
// Upload → row + audit, read back with its chunk count, delete,
// paging bounds, and the web-ingestion SSRF guard (literal
// addresses only, so no test touches the network).
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniSift.Api.Data;
using OmniSift.Api.Models;
using OmniSift.Shared.DTOs;

namespace OmniSift.IntegrationTests;

public sealed class DataSourceLifecycleTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DataSourceLifecycleTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Upload_Pdf_Returns202PendingAndRecordsRowAndAudit()
    {
        var tenantId = Guid.NewGuid();

        var response = await UploadAsync(tenantId, "census-1901.pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = (await response.Content.ReadFromJsonAsync<IngestionResponse>())!;
        body.Status.Should().Be("pending");

        // The background worker may already be processing it: check identity, not status.
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        var row = await db.DataSources.SingleAsync(ds => ds.Id == body.DataSourceId);
        row.TenantId.Should().Be(tenantId);
        row.SourceType.Should().Be("pdf");
        row.FileName.Should().Be("census-1901.pdf");
        (await db.AuditLogs.SingleAsync(a => a.ResourceId == body.DataSourceId)).Action.Should().Be("upload");
    }

    [Theory]
    [InlineData("records.pdf", "pdf")]
    [InlineData("texts.csv", "sms")]
    [InlineData("texts.json", "sms")]
    [InlineData("page.html", "web")]
    [InlineData("letter.docx", "docx")]
    [InlineData("mail.eml", "email")]
    public async Task Upload_OctetStreamWithKnownExtension_ResolvesSourceTypeFromExtension(string fileName, string sourceType)
    {
        var tenantId = Guid.NewGuid();

        var response = await UploadAsync(tenantId, fileName, "application/octet-stream");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = (await response.Content.ReadFromJsonAsync<IngestionResponse>())!;
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        (await db.DataSources.SingleAsync(ds => ds.Id == body.DataSourceId)).SourceType.Should().Be(sourceType);
    }

    [Fact]
    public async Task Upload_OctetStreamWithoutExtension_Returns400ProblemAndNoRow()
    {
        var tenantId = Guid.NewGuid();

        var response = await UploadAsync(tenantId, "README", "application/octet-stream");

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        (await CountDataSourcesAsync(tenantId)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_FileNameAtColumnWidth_Returns202()
    {
        // 512 is data_sources.file_name's width; 513 is rejected (ErrorHandlingTests).
        var response = await UploadAsync(Guid.NewGuid(), new string('a', 508) + ".pdf", "application/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GetDataSource_OwnRow_ReturnsDtoWithChunkCount()
    {
        var tenantId = Guid.NewGuid();
        var id = await SeedDataSourceAsync(tenantId, chunks: 3);

        var dto = await _factory.CreateTenantClient(tenantId).GetFromJsonAsync<DataSourceDto>($"/api/datasources/{id}");

        dto!.Id.Should().Be(id);
        dto.FileName.Should().Be("parish-register.pdf");
        dto.SourceType.Should().Be("pdf");
        dto.Status.Should().Be("completed");
        dto.ChunkCount.Should().Be(3);
    }

    [Fact]
    public async Task DeleteDataSource_OwnRow_Returns204ThenGetAndDeleteReturn404()
    {
        var tenantId = Guid.NewGuid();
        var id = await SeedDataSourceAsync(tenantId, chunks: 2);
        var client = _factory.CreateTenantClient(tenantId);

        (await client.DeleteAsync($"/api/datasources/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await ShouldBeProblemAsync(await client.GetAsync($"/api/datasources/{id}"), HttpStatusCode.NotFound);
        await ShouldBeProblemAsync(await client.DeleteAsync($"/api/datasources/{id}"), HttpStatusCode.NotFound);
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        (await db.AuditLogs.SingleAsync(a => a.ResourceId == id)).Action.Should().Be("delete");
    }

    [Fact]
    public async Task ListDataSources_WithoutPaging_ReturnsDefaultPageOf200()
    {
        var tenantId = Guid.NewGuid();
        await SeedManyAsync(tenantId, 201);

        var page = await _factory.CreateTenantClient(tenantId).GetFromJsonAsync<List<DataSourceDto>>("/api/datasources");

        page.Should().HaveCount(200);
    }

    [Fact]
    public async Task ListDataSources_TakeAtMaximum_Returns200()
    {
        var tenantId = Guid.NewGuid();
        await SeedManyAsync(tenantId, 3);

        var page = await _factory.CreateTenantClient(tenantId)
            .GetFromJsonAsync<List<DataSourceDto>>("/api/datasources?take=500");

        page.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListDataSources_TakeOne_ReturnsNewest()
    {
        var tenantId = Guid.NewGuid();
        await SeedManyAsync(tenantId, 3);

        var page = await _factory.CreateTenantClient(tenantId)
            .GetFromJsonAsync<List<DataSourceDto>>("/api/datasources?take=1");

        page.Should().ContainSingle().Which.FileName.Should().Be("2.pdf");
    }

    [Theory]
    [InlineData("take=501", "take")]
    [InlineData("skip=-1", "skip")]
    public async Task ListDataSources_OutOfRangePaging_Returns400ValidationProblemNamingTheParameter(string query, string parameter)
    {
        var response = await _factory.CreateTenantClient(Guid.NewGuid()).GetAsync($"/api/datasources?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.Should().Contain(k => k.Equals(parameter, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("ftp://example.com/records.html")]
    public async Task WebIngestion_BlockedTarget_Returns400ProblemAndNoRow(string url)
    {
        var tenantId = Guid.NewGuid();

        var response = await _factory.CreateTenantClient(tenantId)
            .PostAsJsonAsync("/api/datasources/web", new WebIngestionRequest { Url = url });

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        (await CountDataSourcesAsync(tenantId)).Should().Be(0);
    }

    private async Task<HttpResponseMessage> UploadAsync(Guid tenantId, string fileName, string contentType)
    {
        var file = new ByteArrayContent("%PDF-1.4 not really a pdf"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var content = new MultipartFormDataContent { { file, "file", fileName } };
        return await _factory.CreateTenantClient(tenantId).PostAsync("/api/datasources/upload", content);
    }

    private async Task<int> CountDataSourcesAsync(Guid tenantId)
    {
        using var scope = _factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>().DataSources.CountAsync();
    }

    private async Task<Guid> SeedDataSourceAsync(Guid tenantId, int chunks)
    {
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        var source = new DataSource
        {
            TenantId = tenantId,
            SourceType = "pdf",
            FileName = "parish-register.pdf",
            Status = IngestionStatus.Completed
        };
        db.DataSources.Add(source);
        db.DocumentChunks.AddRange(Enumerable.Range(0, chunks).Select(i =>
            new DocumentChunk { TenantId = tenantId, DataSourceId = source.Id, Content = $"page {i}", ChunkIndex = i }));
        await db.SaveChangesAsync();
        return source.Id;
    }

    /// <summary>Seeds <paramref name="count"/> rows named 0.pdf (oldest) to n-1.pdf (newest).</summary>
    private async Task SeedManyAsync(Guid tenantId, int count)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        db.DataSources.AddRange(Enumerable.Range(0, count).Select(i => new DataSource
        {
            TenantId = tenantId,
            SourceType = "pdf",
            FileName = $"{i}.pdf",
            CreatedAt = start.AddMinutes(i)
        }));
        await db.SaveChangesAsync();
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be((int)status);
    }
}
