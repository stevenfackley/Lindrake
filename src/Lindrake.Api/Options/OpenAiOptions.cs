// ============================================================
// Lindrake.Api — OpenAI Configuration Options
// ============================================================

namespace Lindrake.Api.Options;

/// <summary>
/// Strongly-typed options for the OpenAI API.
/// Bound from the "OpenAI" configuration section.
/// </summary>
public sealed class OpenAiOptions
{
    public const string Section = "OpenAI";

    /// <summary>
    /// Required only when Embedding:Provider is OpenAi (enforced by
    /// <see cref="EmbeddingOptionsValidator"/>).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string EmbeddingModel { get; set; } = "text-embedding-3-large";

    public int EmbeddingDimensions { get; set; } = 3072;
}
