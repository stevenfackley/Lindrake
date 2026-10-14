// ============================================================
// OmniSift.Api — Text Chunker Interface & Implementation
// Splits text into overlapping, token-budgeted chunks for embedding
// ============================================================

using Microsoft.Extensions.Options;
using OmniSift.Api.Options;

namespace OmniSift.Api.Services;

/// <summary>
/// Represents a single text chunk with metadata.
/// </summary>
public sealed record TextChunk
{
    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Zero-based index of this chunk in the sequence.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Token count as measured by the active <see cref="ITokenCounter"/>.
    /// </summary>
    public int TokenCount { get; init; }
}

/// <summary>
/// Splits text into overlapping chunks suitable for vector embedding.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Splits <paramref name="text"/> into ordered chunks of at most
    /// <see cref="ChunkingOptions.MaxTokens"/> tokens, consecutive chunks sharing
    /// up to <see cref="ChunkingOptions.OverlapTokens"/> tokens.
    /// </summary>
    IReadOnlyList<TextChunk> ChunkText(string text);
}

/// <summary>
/// Packs whole sentences (lines and <c>. ! ?</c>-terminated runs) into chunks until
/// the token budget is reached; a sentence larger than the budget is packed word by
/// word. Only a single word larger than the whole budget can yield an oversized chunk.
/// Overlap is made of whole trailing sentences/words of the previous chunk.
/// </summary>
public sealed class TextChunker(ITokenCounter tokenCounter, IOptions<ChunkingOptions> options) : ITextChunker
{
    private readonly int _maxTokens = options.Value.MaxTokens;
    private readonly int _overlapTokens = options.Value.OverlapTokens;

    /// <inheritdoc />
    public IReadOnlyList<TextChunk> ChunkText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var chunks = new List<TextChunk>();
        var window = new List<Unit>();
        var windowTokens = 0;

        foreach (var unit in SplitUnits(text))
        {
            if (window.Count > 0 && windowTokens + unit.Tokens > _maxTokens)
            {
                chunks.Add(ToChunk(window, windowTokens, chunks.Count));
                windowTokens = KeepOverlap(window);

                // The carried overlap must leave room for the new unit, or the next
                // chunk would be nothing but overlap.
                if (windowTokens + unit.Tokens > _maxTokens)
                {
                    window.Clear();
                    windowTokens = 0;
                }
            }

            window.Add(unit);
            windowTokens += unit.Tokens;
        }

        if (window.Count > 0)
            chunks.Add(ToChunk(window, windowTokens, chunks.Count));

        return chunks;
    }

    /// <summary>Sentences within budget, else that sentence's words.</summary>
    private IEnumerable<Unit> SplitUnits(string text)
    {
        foreach (var sentence in SplitSentences(text))
        {
            var tokens = tokenCounter.Count(sentence);
            if (tokens <= _maxTokens)
            {
                yield return new Unit(sentence, tokens);
                continue;
            }

            foreach (var word in sentence.Split(' '))
                yield return new Unit(word, tokenCounter.Count(word));
        }
    }

    /// <summary>
    /// Splits on line breaks and after words ending in <c>. ! ?</c>; whitespace inside
    /// a sentence is collapsed to single spaces.
    /// </summary>
    private static IEnumerable<string> SplitSentences(string text)
    {
        var words = new List<string>();

        foreach (var line in text.Split('\n'))
        {
            foreach (var word in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(word);
                if (word[^1] is '.' or '!' or '?')
                {
                    yield return string.Join(' ', words);
                    words.Clear();
                }
            }

            if (words.Count > 0)
            {
                yield return string.Join(' ', words);
                words.Clear();
            }
        }
    }

    /// <summary>
    /// Drops units from the front of <paramref name="window"/> until what remains
    /// fits the overlap budget; returns the remaining token count.
    /// </summary>
    private int KeepOverlap(List<Unit> window)
    {
        var keepFrom = window.Count;
        var kept = 0;
        while (keepFrom > 0 && kept + window[keepFrom - 1].Tokens <= _overlapTokens)
        {
            keepFrom--;
            kept += window[keepFrom].Tokens;
        }

        window.RemoveRange(0, keepFrom);
        return kept;
    }

    private static TextChunk ToChunk(List<Unit> window, int tokens, int index) => new()
    {
        Content = string.Join(' ', window.Select(u => u.Text)),
        Index = index,
        TokenCount = tokens
    };

    private readonly record struct Unit(string Text, int Tokens);
}
