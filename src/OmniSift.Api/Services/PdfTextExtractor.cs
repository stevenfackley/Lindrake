// ============================================================
// OmniSift.Api — PDF Text Extractor
// Extracts text from PDF files using PdfPig
// ============================================================

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace OmniSift.Api.Services;

/// <summary>
/// Extracts text from PDF documents page-by-page using PdfPig.
/// Concatenates pages with page markers for metadata tracking.
/// </summary>
public sealed class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : ITextExtractor
{
    /// <inheritdoc />
    public string SourceType => SourceTypes.Pdf;

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogDebug("Extracting text from PDF: {FileName}", fileName ?? "unknown");

        // PdfPig requires a seekable stream; buffer if needed
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);

        var pages = new List<string>();

        foreach (Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = page.Text;

            if (!string.IsNullOrWhiteSpace(text))
            {
                pages.Add($"[Page {page.Number}]\n{text.Trim()}");
            }
        }

        if (pages.Count == 0)
        {
            logger.LogWarning("No text extracted from PDF: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        logger.LogInformation(
            "Extracted text from {PageCount} pages of PDF: {FileName}",
            pages.Count, fileName ?? "unknown");

        return string.Join("\n\n", pages);
    }
}
