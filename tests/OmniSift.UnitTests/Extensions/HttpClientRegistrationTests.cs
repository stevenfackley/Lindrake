// ============================================================
// Unit Tests — typed HttpClient registrations
// Resolves the real clients from the service modules and checks
// base address, scheme and auth header of the outgoing request.
// ============================================================

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniSift.Api.Extensions;
using OmniSift.Api.Plugins;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Extensions;

public sealed class HttpClientRegistrationTests
{
    private readonly List<HttpRequestMessage> _requests = [];

    [Fact]
    public async Task OpenAiEmbeddingClient_PostsToOpenAiOverHttpsWithBearerKey()
    {
        using var sp = BuildServices(services => services.AddOmniSiftEmbeddings(), """
            { "data": [ { "index": 0, "embedding": [0.5, 0.25] } ] }
            """);

        var vectors = await sp.GetRequiredService<OpenAIEmbeddingService>().GenerateEmbeddingsAsync(["hi"]);

        vectors.Should().ContainSingle();
        var request = _requests.Should().ContainSingle().Subject;
        request.RequestUri.Should().Be(new Uri("https://api.openai.com/v1/embeddings"));
        request.Headers.Authorization!.ToString().Should().Be("Bearer sk-openai");
    }

    [Fact]
    public async Task TavilyClient_PostsToTavilyWithBearerKeyNotBodyKey()
    {
        using var sp = BuildServices(services => services.AddOmniSiftAgent(), """
            { "answer": "42", "results": [ { "title": "T", "url": "https://x.test", "content": "c", "score": 0.9 } ] }
            """);
        using var scope = sp.CreateScope();

        var output = await scope.ServiceProvider.GetRequiredService<WebScraperPlugin>().SearchWebAsync("meaning");

        output.Should().Contain("\"answer\": \"42\"").And.Contain("\"rank\": 1");
        var request = _requests.Should().ContainSingle().Subject;
        request.RequestUri.Should().Be(new Uri("https://api.tavily.com/search"));
        request.Headers.Authorization!.ToString().Should().Be("Bearer tvly-key");
        (await request.Content!.ReadAsStringAsync()).Should().NotContain("api_key");
    }

    [Fact]
    public async Task WaybackClient_UsesHttps()
    {
        using var sp = BuildServices(services => services.AddOmniSiftAgent(), """{ "archived_snapshots": {} }""");
        using var scope = sp.CreateScope();

        await scope.ServiceProvider.GetRequiredService<WaybackMachinePlugin>().GetArchivedPageAsync("http://example.com");

        var request = _requests.Should().ContainSingle().Subject;
        request.RequestUri!.Scheme.Should().Be(Uri.UriSchemeHttps);
        request.RequestUri.GetLeftPart(UriPartial.Path).Should().Be("https://archive.org/wayback/available");
    }

    private ServiceProvider BuildServices(Action<IServiceCollection> module, string responseJson)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "sk-openai",
                ["Tavily:ApiKey"] = "tvly-key",
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(config);
        module(services);
        services.ConfigureHttpClientDefaults(b =>
            b.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_requests, responseJson)));

        return services.BuildServiceProvider();
    }

    private sealed class StubHandler(List<HttpRequestMessage> requests, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
