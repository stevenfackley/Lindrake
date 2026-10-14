// ============================================================
// OmniSift.Api — Web Scraper Plugin for Semantic Kernel
// Searches the web via Tavily API for real-time information
// ============================================================

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OmniSift.Api.Options;
using OmniSift.Api.Services;

namespace OmniSift.Api.Plugins;

/// <summary>
/// Semantic Kernel plugin that searches the web using Tavily API.
/// The agent invokes this to find current information not available
/// in the tenant's uploaded documents.
/// Also records results in <see cref="ICitationAccumulator"/>.
/// The typed HttpClient carries <see cref="TavilyBaseAddress"/> and the bearer
/// key (configured in AddOmniSiftAgent); requests use relative URIs.
/// </summary>
public sealed partial class WebScraperPlugin(
    HttpClient httpClient,
    IOptions<TavilyOptions> tavilyOptions,
    ICitationAccumulator citations,
    ILogger<WebScraperPlugin> logger)
{
    public static readonly Uri TavilyBaseAddress = new("https://api.tavily.com/");

    private readonly bool _configured = !string.IsNullOrWhiteSpace(tavilyOptions.Value.ApiKey);

    [KernelFunction("SearchWeb")]
    [Description("Searches the web for current information related to the query. " +
                 "Returns titles, URLs, and content snippets from web pages. " +
                 "Use this when the user's question requires information beyond what's in their uploaded documents.")]
    public async Task<string> SearchWebAsync(
        [Description("The search query to find information on the web")] string query,
        [Description("Maximum number of results to return (default: 5)")] int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "No query provided for web search.";

        if (!_configured)
        {
            logger.LogWarning("Tavily API key not configured; web search unavailable");
            return "Web search is not available — API key not configured.";
        }

        maxResults = Math.Clamp(maxResults, 1, 10);

        // Query text is derived from user content (often PII) — log its size only.
        logger.LogDebug("WebSearch: queryLength={QueryLength}, maxResults={MaxResults}", query.Length, maxResults);

        try
        {
            var request = new TavilySearchRequest
            {
                Query = query,
                MaxResults = maxResults,
                SearchDepth = "basic",
                IncludeAnswer = true
            };

            var response = await httpClient.PostAsJsonAsync(
                "search",
                request,
                TavilyJsonContext.Default.TavilySearchRequest).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(
                TavilyJsonContext.Default.TavilySearchResponse).ConfigureAwait(false);

            if (result?.Results is null || result.Results.Count == 0)
            {
                return "No web results found for this query.";
            }

            var formattedResults = new SearchOutput(
                result.Answer,
                [.. result.Results.Select((r, i) => new SearchOutputItem(
                    Rank: i + 1,
                    Title: r.Title,
                    Url: r.Url,
                    Snippet: r.Content?.Length > 500 ? r.Content[..500] + "..." : r.Content,
                    Score: r.Score))]);

            logger.LogInformation("WebSearch returned {Count} results for query", result.Results.Count);

            // Record citations
            foreach (var r in result.Results)
            {
                var snippet = r.Content?.Length > 300 ? r.Content[..300] + "…" : r.Content;
                citations.AddWebResult(r.Url, r.Title, r.Score, snippet);
            }

            return JsonSerializer.Serialize(formattedResults, TavilyJsonContext.Default.SearchOutput);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebSearch failed for query: {Query}", query);
            return $"Error performing web search: {ex.Message}";
        }
    }

    // ── Tavily API DTOs ─────────────────────────────

    [JsonSerializable(typeof(TavilySearchRequest))]
    [JsonSerializable(typeof(TavilySearchResponse))]
    [JsonSerializable(typeof(SearchOutput))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    private sealed partial class TavilyJsonContext : JsonSerializerContext;

    /// <summary>Tool output handed back to the LLM.</summary>
    private sealed record SearchOutput(string? Answer, IReadOnlyList<SearchOutputItem> Results);

    private sealed record SearchOutputItem(int Rank, string Title, string Url, string? Snippet, double Score);

    private sealed class TavilySearchRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; init; } = string.Empty;

        [JsonPropertyName("max_results")]
        public int MaxResults { get; init; } = 5;

        [JsonPropertyName("search_depth")]
        public string SearchDepth { get; init; } = "basic";

        [JsonPropertyName("include_answer")]
        public bool IncludeAnswer { get; init; } = true;
    }

    private sealed class TavilySearchResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; init; }

        [JsonPropertyName("results")]
        public List<TavilyResult> Results { get; init; } = [];
    }

    private sealed class TavilyResult
    {
        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("score")]
        public double Score { get; init; }
    }
}
