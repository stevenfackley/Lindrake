// ============================================================
// Lindrake.Api — Plain Text Extractor
// Reads .txt and .md files as-is (Markdown is kept verbatim:
// its syntax is readable text and chunks fine).
// ============================================================

using System.Text;

namespace Lindrake.Api.Services;

/// <summary>
/// Extracts text from plain-text and Markdown files. Decodes UTF-8 unless a
/// byte-order mark names another Unicode encoding (UTF-16/32 exports), and
/// normalizes every line ending to <c>\n</c>.
/// </summary>
public sealed class PlainTextExtractor(ILogger<PlainTextExtractor> logger) : ITextExtractor
{
    /// <inheritdoc />
    public string SourceType => SourceTypes.Text;

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogDebug("Extracting plain text: {FileName}", fileName ?? "unknown");

        // A BOM, when present, wins over the UTF-8 default and is not returned as text.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var text = content.ReplaceLineEndings("\n").Trim();
        if (text.Length == 0)
        {
            logger.LogWarning("No text extracted from plain-text file: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        logger.LogInformation(
            "Extracted {Length} chars from plain-text file: {FileName}",
            text.Length, fileName ?? "unknown");

        return text;
    }
}
