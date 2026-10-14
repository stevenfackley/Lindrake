// ============================================================
// Unit Tests — WebPageFetcher
// The blocked-address cases run the real primary handler: its
// ConnectCallback rejects before any socket is opened, so no
// network access is needed.
// ============================================================

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSift.Api.Extensions;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Services.Ingestion;

namespace OmniSift.UnitTests.Services;

public sealed class WebPageFetcherTests
{
    [Theory]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not-a-url")]
    public async Task FetchAsync_NonHttpUrl_IsBlockedWithoutARequest(string url)
    {
        var handler = new RecordingHandler();
        var fetcher = new WebPageFetcher(new HttpClient(handler), NullLogger<WebPageFetcher>.Instance);

        var act = () => fetcher.FetchAsync(url);

        await act.Should().ThrowAsync<BlockedUrlException>();
        handler.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://localhost:9/")]
    [InlineData("http://[::1]:9/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/")]
    public async Task FetchAsync_InternalAddress_IsBlockedByTheConnectGuard(string url)
    {
        using var client = new HttpClient(WebPageFetcher.CreatePrimaryHandler());
        var fetcher = new WebPageFetcher(client, NullLogger<WebPageFetcher>.Instance);

        var act = () => fetcher.FetchAsync(url);

        (await act.Should().ThrowAsync<BlockedUrlException>())
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FetchAsync_ThroughRegisteredClient_InternalAddressIsBlocked()
    {
        // Full typed-client pipeline (resilience handler included): the blocked
        // connection must surface as BlockedUrlException, not a 502-style failure.
        using var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddOmniSiftIngestion()
            .BuildServiceProvider();

        var act = () => sp.GetRequiredService<IWebPageFetcher>().FetchAsync("http://127.0.0.1:9/");

        await act.Should().ThrowAsync<BlockedUrlException>();
    }

    [Fact]
    public async Task FetchAsync_Success_ReturnsBody()
    {
        var handler = new RecordingHandler("<html>hi</html>");
        var fetcher = new WebPageFetcher(new HttpClient(handler), NullLogger<WebPageFetcher>.Instance);

        var body = await fetcher.FetchAsync("https://example.com/");

        System.Text.Encoding.UTF8.GetString(body).Should().Be("<html>hi</html>");
    }

    [Fact]
    public async Task FetchAsync_BodyOverCap_Throws()
    {
        var handler = new RecordingHandler(new string('x', 2048));
        var client = new HttpClient(handler) { MaxResponseContentBufferSize = 1024 };
        var fetcher = new WebPageFetcher(client, NullLogger<WebPageFetcher>.Instance);

        var act = () => fetcher.FetchAsync("https://example.com/");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class RecordingHandler(string body = "") : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
