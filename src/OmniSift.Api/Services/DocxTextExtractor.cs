// ============================================================
// OmniSift.Api — DOCX Text Extractor
// Extracts text from Word .docx files using DocumentFormat.OpenXml
// ============================================================

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OmniSift.Api.Services;

/// <summary>
/// Extracts readable text from Word .docx documents.
/// Walks the main document body paragraph-by-paragraph, preserving
/// paragraph breaks and discarding drawing/image nodes.
/// </summary>
public sealed class DocxTextExtractor(ILogger<DocxTextExtractor> logger) : ITextExtractor
{
    /// <inheritdoc />
    public string SourceType => SourceTypes.Docx;

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogDebug("Extracting text from DOCX: {FileName}", fileName ?? "unknown");

        // WordprocessingDocument.Open requires a seekable stream; buffer if needed.
        MemoryStream? owned = null;
        Stream seekable = stream;

        if (!stream.CanSeek)
        {
            owned = new MemoryStream();
            await stream.CopyToAsync(owned, cancellationToken).ConfigureAwait(false);
            owned.Position = 0;
            seekable = owned;
        }

        try
        {
            using var doc = WordprocessingDocument.Open(seekable, isEditable: false);

            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                logger.LogWarning("DOCX has no body: {FileName}", fileName ?? "unknown");
                return string.Empty;
            }

            var sb = new StringBuilder();

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Concatenate all Run text within this paragraph.
                var paraText = string.Concat(
                    paragraph.Descendants<Text>().Select(t => t.Text));

                if (!string.IsNullOrWhiteSpace(paraText))
                {
                    sb.AppendLine(paraText.Trim());
                }
            }

            var result = sb.ToString().Trim();

            if (result.Length == 0)
            {
                logger.LogWarning("No text extracted from DOCX: {FileName}", fileName ?? "unknown");
                return string.Empty;
            }

            logger.LogInformation(
                "Extracted {Length} chars from DOCX: {FileName}",
                result.Length, fileName ?? "unknown");

            return result;
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
