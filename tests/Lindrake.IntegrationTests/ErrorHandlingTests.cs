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
    public async Task Upload_SourceTypeLongerThanColumn_Returns400Validation()
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent("hello"u8.ToArray()), "file", "notes.pdf" },
            { new StringContent(new string('x', 51)), "sourceType" }
        };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("sourceType");
    }

    [Fact]
    public async Task Upload_FileNameLongerThanColumn_Returns400Problem()
    {
        var file = new ByteArrayContent("%PDF-1.4"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var content = new MultipartFormDataContent { { file, "file", new string('a', 509) + ".pdf" } };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("File name");
    }

    [Fact]
    public async Task Upload_LegacyWordDoc_Returns400ProblemInsteadOfFailingInIngestion()
    {
        // OLE compound-file header: a binary .doc, which DocxTextExtractor cannot open.
        var file = new ByteArrayContent([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/msword");
        using var content = new MultipartFormDataContent { { file, "file", "letter.doc" } };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Unsupported file type");
    }

    [Fact]
    public async Task Upload_OctetStreamWithUnknownExtension_Returns400Problem()
    {
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var content = new MultipartFormDataContent { { file, "file", "archive.zip" } };

        var response = await _client.PostAsync("/api/datasources/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Contain("Unsupported file type");
    }
}
