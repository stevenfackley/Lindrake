// ============================================================
// OmniSift.Api — Token counting for chunk sizing
// ============================================================

namespace OmniSift.Api.Services;

/// <summary>
/// Counts tokens the way the active embedding model sees them, so chunks can be
/// sized to fit its input window.
/// </summary>
public interface ITokenCounter
{
    /// <summary>Number of tokens in <paramref name="text"/>.</summary>
    int Count(string text);
}

/// <summary>
/// Approximation for providers without a local tokenizer (OpenAI): ~1.3 tokens
/// per whitespace-separated word for English text.
/// </summary>
public sealed class WordHeuristicTokenCounter : ITokenCounter
{
    private const double TokensPerWord = 1.3;

    /// <inheritdoc />
    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Ceiling(words * TokensPerWord);
    }
}
