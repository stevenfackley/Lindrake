// ============================================================
// Unit Tests — WaybackMachinePlugin
// Verifies API response handling and error cases
// ============================================================

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using OmniSift.Api.Plugins;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Plugins;

public sealed class WaybackMachinePluginTests
{
    [Fact]
    public async Task GetArchivedPage_WithValidSnapshot_ReturnsSnapshotInfo()
    {
        var responseJson = """
            {
                "archived_snapshots": {
                    "closest": {
                        "url": "http://web.archive.org/web/20240101/http://example.com",
                        "timestamp": "20240101120000",
                        "available": true,
                        "status": "200"
                    }
                }
            }
            """;

        var plugin = CreatePlugin(HttpStatusCode.OK, responseJson);
        var result = await plugin.GetArchivedPageAsync("http://example.com");

        result.Should().Contain("found");
        result.Should().Contain("web.archive.org");
        result.Should().Contain("20240101120000");

        var parsed = JsonDocument.Parse(result);
        parsed.RootElement.GetProperty("found").GetBoolean().Should().BeTrue();
        parsed.RootElement.GetProperty("archiveUrl").GetString().Should().Contain("web.archive.org");
    }

    [Fact]
    public async Task GetArchivedPage_WithNoSnapshot_ReturnsNotFound()
    {
        var responseJson = """{ "archived_snapshots": {} }""";

        var plugin = CreatePlugin(HttpStatusCode.OK, responseJson);
        var result = await plugin.GetArchivedPageAsync("http://nonexistent.example.com");

        var parsed = JsonDocument.Parse(result);
        parsed.RootElement.GetProperty("found").GetBoolean().Should().BeFalse();
        parsed.RootElement.GetProperty("message").GetString().Should().Contain("No archived snapshot");
    }

    [Fact]
    public async Task GetArchivedPage_WithEmptyUrl_ReturnsErrorMessage()
    {
        var plugin = CreatePlugin(HttpStatusCode.OK, "{}");
        var result = await plugin.GetArchivedPageAsync(string.Empty);

        result.Should().Contain("No URL provided");
    }

    [Fact]
    public async Task GetArchivedPage_WhenApiFails_ReturnsErrorMessage()
    {
        var plugin = CreatePlugin(HttpStatusCode.InternalServerError, "Server Error");
        var result = await plugin.GetArchivedPageAsync("http://example.com");

        result.Should().Contain("Error querying Wayback Machine");
    }

    private static WaybackMachinePlugin CreatePlugin(HttpStatusCode statusCode, string content)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = WaybackMachinePlugin.BaseAddress };
        return new WaybackMachinePlugin(
            httpClient,
            Mock.Of<ICitationAccumulator>(),
            Mock.Of<ILogger<WaybackMachinePlugin>>());
    }
}
