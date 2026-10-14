// ============================================================
// Integration Tests — MVC JSON serialization
// Controllers serialize DTOs through the source-generated
// OmniSiftJsonContext with camelCase names; types outside it
// (ProblemDetails, anonymous objects) still fall back to reflection.
// ============================================================

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OmniSift.Api.Data;
using OmniSift.Api.Models;
using OmniSift.Shared;

namespace OmniSift.IntegrationTests;

public sealed class JsonSerializationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public JsonSerializationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void MvcJsonOptions_UseSourceGeneratedContextFirst()
    {
        var options = _factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        options.TypeInfoResolverChain.Should().HaveCountGreaterThan(1, "reflection fallback must remain for ProblemDetails");
        options.TypeInfoResolverChain[0].Should().BeSameAs(OmniSiftJsonContext.Default);
    }

    [Fact]
    public async Task DataSourceDto_IsSerializedCamelCase()
    {
        var tenantId = Guid.NewGuid();
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
            var source = new DataSource
            {
                TenantId = tenantId,
                SourceType = "pdf",
                FileName = "camel.pdf",
                Status = IngestionStatus.Completed
            };
            db.DataSources.Add(source);
            await db.SaveChangesAsync();
            id = source.Id;
        }

        var response = await _factory.CreateTenantClient(tenantId).GetAsync($"/api/datasources/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("fileName").GetString().Should().Be("camel.pdf");
        json.RootElement.GetProperty("sourceType").GetString().Should().Be("pdf");
        json.RootElement.TryGetProperty("FileName", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ProblemDetails_StillSerializeViaReflectionFallback()
    {
        var response = await _factory.CreateTenantClient().GetAsync($"/api/datasources/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        json.RootElement.GetProperty("detail").GetString().Should().Contain("not found");
    }
}
