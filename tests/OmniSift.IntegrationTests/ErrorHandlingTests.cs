// ============================================================
// Integration Tests — Error responses
// Domain failures surface as RFC 7807 ProblemDetails with their
// own status code.
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace OmniSift.IntegrationTests;

public sealed class ErrorHandlingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ErrorHandlingTests(CustomWebApplicationFactory factory) => _client = factory.CreateTenantClient();

    [Fact]
    public async Task EntityExtraction_WithoutLlm_Returns503Problem()
    {
        // The test host's kernel has no chat-completion service.
        var response = await _client.PostAsync("/api/entities/extract", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("LLM service not configured");
        problem.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task Upload_WithUnknownExplicitSourceType_Returns400Problem()
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent("hello"u8.ToArray()), "file", "notes.txt" },
            { new StringContent("spreadsheet"), "sourceType" }
        };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Unsupported source type: 'spreadsheet'");
    }

    [Fact]
    public async Task Upload_OctetStreamWithUnknownExtension_Returns400Problem()
    {
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var content = new MultipartFormDataContent { { file, "file", "notes.txt" } };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Unsupported file type");
    }
}
