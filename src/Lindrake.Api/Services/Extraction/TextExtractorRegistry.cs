// ============================================================
// Lindrake.Api — Text extractor registry
// The single source of truth for which source types can be
// ingested and how an upload maps to one.
// ============================================================

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Lindrake.Shared.DTOs;

namespace Lindrake.Api.Services.Extraction;

/// <summary>Looks up the <see cref="ITextExtractor"/> for a source type.</summary>
public interface ITextExtractorRegistry
{
    /// <summary>Registered source types, sorted.</summary>
    IReadOnlyList<string> SupportedSourceTypes { get; }

    /// <summary>Finds the extractor for <paramref name="sourceType"/> (case-insensitive).</summary>
    bool TryGet(string? sourceType, [NotNullWhen(true)] out ITextExtractor? extractor);

    /// <summary>
    /// Maps an upload to a registered source type: by MIME type, or — when the MIME
    /// type is missing or the generic <c>application/octet-stream</c> — by the file
    /// name's extension.
    /// </summary>
    bool TryResolveSourceType(string? contentType, string? fileName, [NotNullWhen(true)] out string? sourceType);
}

/// <summary>
/// Default <see cref="ITextExtractorRegistry"/>, built from every registered
/// <see cref="ITextExtractor"/> keyed by its <see cref="ITextExtractor.SourceType"/>.
/// </summary>
public sealed class TextExtractorRegistry : ITextExtractorRegistry
{
    private const string OctetStream = "application/octet-stream";

    // No application/msword (.doc): that is the binary Word format, which the
    // OpenXml-based docx extractor cannot open. Unmapped = rejected with a 400 at
    // upload instead of accepted and then failed in background ingestion.
    private static readonly FrozenDictionary<string, string> SourceTypeByMimeType =
        new Dictionary<string, string>
        {
            ["application/pdf"] = SourceTypes.Pdf,
            ["text/csv"] = SourceTypes.Sms,
            ["application/json"] = SourceTypes.Sms,
            ["text/html"] = SourceTypes.Web,
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = SourceTypes.Docx,
            ["message/rfc822"] = SourceTypes.Email,
            ["application/mbox"] = SourceTypes.Email,
            ["text/plain"] = SourceTypes.Text,
            ["text/markdown"] = SourceTypes.Text
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Each upload format's extension resolves to the source type of the MIME type
    // the client sends for it. Declared after SourceTypeByMimeType (static fields
    // initialize in order); a format whose MIME type is unmapped fails type init.
    private static readonly FrozenDictionary<string, string> SourceTypeByExtension =
        UploadFormats.MimeTypeByExtension.ToFrozenDictionary(
            format => format.Key,
            format => SourceTypeByMimeType[format.Value],
            StringComparer.OrdinalIgnoreCase);

    private readonly FrozenDictionary<string, ITextExtractor> _extractors;

    public TextExtractorRegistry(IEnumerable<ITextExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);

        // ToFrozenDictionary throws on a duplicate SourceType — two extractors
        // claiming the same key is a registration bug, caught at first resolve.
        _extractors = extractors.ToFrozenDictionary(e => e.SourceType, StringComparer.OrdinalIgnoreCase);
        SupportedSourceTypes = [.. _extractors.Keys.Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedSourceTypes { get; }

    /// <inheritdoc />
    public bool TryGet(string? sourceType, [NotNullWhen(true)] out ITextExtractor? extractor)
    {
        extractor = null;
        return !string.IsNullOrWhiteSpace(sourceType) && _extractors.TryGetValue(sourceType, out extractor);
    }

    /// <inheritdoc />
    public bool TryResolveSourceType(
        string? contentType,
        string? fileName,
        [NotNullWhen(true)] out string? sourceType)
    {
        sourceType = null;

        // MediaType drops parameters: "text/csv; charset=utf-8" → "text/csv".
        var mediaType = MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed.MediaType : null;

        string? candidate;
        if (string.IsNullOrEmpty(mediaType) || mediaType.Equals(OctetStream, StringComparison.OrdinalIgnoreCase))
        {
            // Generic or missing MIME type (the Web client falls back to octet-stream
            // for a file it has no type for) — fall back to the file extension.
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension) || !SourceTypeByExtension.TryGetValue(extension, out candidate))
                return false;
        }
        else if (!SourceTypeByMimeType.TryGetValue(mediaType, out candidate))
        {
            return false;
        }

        // Only report types that actually have an extractor registered.
        if (!_extractors.ContainsKey(candidate))
            return false;

        sourceType = candidate;
        return true;
    }
}
