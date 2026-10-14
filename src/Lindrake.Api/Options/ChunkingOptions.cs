// ============================================================
// Lindrake.Api — Chunking configuration options
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace Lindrake.Api.Options;

/// <summary>
/// Chunk sizing, in tokens of the active embedding model (see ITokenCounter).
/// Bound from the "Chunking" configuration section.
/// </summary>
/// <remarks>
/// The defaults fit bge-small-en-v1.5's 512-token window with room for the
/// [CLS]/[SEP] special tokens. Keep <see cref="MaxTokens"/> below
/// <c>Embedding:MaxSequenceLength</c>, or the tail of each chunk is truncated
/// away before embedding.
/// </remarks>
public sealed class ChunkingOptions : IValidatableObject
{
    public const string Section = "Chunking";

    /// <summary>Token budget per chunk.</summary>
    [Range(16, 8192)]
    public int MaxTokens { get; set; } = 480;

    /// <summary>Tokens repeated from the end of one chunk at the start of the next.</summary>
    [Range(0, 8191)]
    public int OverlapTokens { get; set; } = 48;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OverlapTokens >= MaxTokens)
        {
            yield return new ValidationResult(
                $"{nameof(OverlapTokens)} must be less than {nameof(MaxTokens)}.",
                [nameof(OverlapTokens)]);
        }
    }
}
