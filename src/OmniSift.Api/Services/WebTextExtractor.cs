// ============================================================
// OmniSift.Api — Web/HTML Text Extractor
// Extracts readable text from HTML using HtmlAgilityPack
// ============================================================

using System.Text;
using HtmlAgilityPack;

namespace OmniSift.Api.Services;

/// <summary>
/// Extracts readable text from HTML pages using HtmlAgilityPack.
/// Strips scripts, styles, and navigation elements; prioritizes
/// article/main content areas.
/// </summary>
public sealed class WebTextExtractor(ILogger<WebTextExtractor> logger) : ITextExtractor
{
    /// <summary>
    /// HTML tags that should be completely removed (including their content).
    /// </summary>
    private static readonly HashSet<string> RemoveTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "nav", "footer", "header", "noscript",
        "svg", "iframe", "form", "button", "input", "select", "textarea"
    };

    /// <summary>
    /// Content-priority selectors — text is preferentially extracted from these.
    /// </summary>
    private static readonly string[] ContentSelectors =
    [
        "//article",
        "//main",
        "//*[@role='main']",
        "//*[contains(@class,'article')]",
        "//*[contains(@class,'content')]",
        "//*[contains(@class,'post')]"
    ];

    /// <inheritdoc />
    public string SourceType => SourceTypes.Web;

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogDebug("Extracting text from HTML: {FileName}", fileName ?? "unknown");

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(html))
        {
            logger.LogWarning("Empty HTML content: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove unwanted tags entirely
        HtmlText.RemoveNodes(doc, RemoveTags);

        // Try to extract from content-priority areas first
        var text = ExtractFromContentAreas(doc);

        // Fall back to full body text
        if (string.IsNullOrWhiteSpace(text))
        {
            var body = doc.DocumentNode.SelectSingleNode("//body");
            text = HtmlText.CleanText(body ?? doc.DocumentNode);
        }

        // Extract title for context
        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            text = $"[Title: {HtmlEntity.DeEntitize(title)}]\n\n{text}";
        }

        logger.LogInformation(
            "Extracted {Length} chars from HTML: {FileName}",
            text.Length, fileName ?? "unknown");

        return text;
    }

    private static string? ExtractFromContentAreas(HtmlDocument doc)
    {
        foreach (var xpath in ContentSelectors)
        {
            var node = doc.DocumentNode.SelectSingleNode(xpath);
            if (node is not null)
            {
                var text = HtmlText.CleanText(node);
                if (!string.IsNullOrWhiteSpace(text) && text.Length > 100)
                {
                    return text;
                }
            }
        }

        return null;
    }
}
