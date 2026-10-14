// ============================================================
// Integration Tests — /api/agent/report and /api/agent/report/pdf
// ============================================================

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniSift.Shared.DTOs;

namespace OmniSift.IntegrationTests;

public sealed class ReportEndpointTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 5, TimeSpan.Zero);

    private static readonly GenerateReportRequest Request = new()
    {
        Messages =
        [
            new ReportTurn { Role = "user", Content = "Where was Michael born?" },
            new ReportTurn
            {
                Role = "assistant",
                Content = "Cork.",
                Citations = [new SourceCitation { Type = "web", Title = "Census", Url = "https://example.test", RelevanceScore = 0.5 }]
            }
        ]
    };

    private readonly CustomWebApplicationFactory _factory = new()
    {
        ConfigureTestServices = services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        }
    };

    [Fact]
    public async Task Report_ReturnsMarkdownStampedWithTheClock()
    {
        var response = await _factory.CreateTenantClient().PostAsJsonAsync("/api/agent/report", Request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GenerateReportResponse>();
        body!.Title.Should().Be("Research Report");
        body.GeneratedAt.Should().Be("2026-06-21T12:00:05Z");
        body.Markdown.Should().StartWith("# Research Report")
            .And.Contain("*Generated: 2026-06-21T12:00:05Z*")
            .And.Contain("score: 0.500");
    }

    [Fact]
    public async Task ReportPdf_ReturnsPdfFileNamedAfterTheClock()
    {
        var response = await _factory.CreateTenantClient().PostAsJsonAsync("/api/agent/report/pdf", Request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("report-20260621120005.pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    public void Dispose() => _factory.Dispose();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
