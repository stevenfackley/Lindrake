// ============================================================
// OmniSift.Api — Tavily Web Search Configuration Options
// ============================================================

namespace OmniSift.Api.Options;

/// <summary>
/// Strongly-typed options for the Tavily web search API.
/// Bound from the "Tavily" configuration section.
/// </summary>
public sealed class TavilyOptions
{
    public const string Section = "Tavily";

    public string ApiKey { get; set; } = string.Empty;
}
