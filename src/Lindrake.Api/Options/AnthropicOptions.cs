// ============================================================
// Lindrake.Api — Anthropic Configuration Options
// ============================================================

namespace Lindrake.Api.Options;

/// <summary>
/// Strongly-typed options for the Anthropic API.
/// Bound from the "Anthropic" configuration section.
/// </summary>
public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>
    /// Optional. Empty disables the LLM: agent/entity endpoints answer 503.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string ModelId { get; set; } = "claude-sonnet-4-20250514";
}
