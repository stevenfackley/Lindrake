// ============================================================
// Lindrake.Web — MIME type of an uploaded file
// ============================================================

using System.Collections.Frozen;

namespace Lindrake.Web.Services;

/// <summary>
/// The MIME type to send with an upload. Browsers report nothing (or something
/// generic) for many of the formats Lindrake ingests, so the extension decides
/// for those; the table matches the API's TextExtractorRegistry.
/// </summary>
public static class UploadContentTypes
{
    /// <summary>Sent when nothing better is known; the API then resolves by extension.</summary>
    public const string Fallback = "application/octet-stream";

    private static readonly FrozenDictionary<string, string> ByExtension =
        new Dictionary<string, string>
        {
            [".pdf"] = "application/pdf",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".eml"] = "message/rfc822",
            [".mbox"] = "application/mbox",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The MIME type for <paramref name="fileName"/>: by extension for the ingestible
    /// formats, else <paramref name="browserContentType"/>, else <see cref="Fallback"/>.
    /// </summary>
    public static string ForFileName(string fileName, string? browserContentType = null)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension) && ByExtension.TryGetValue(extension, out var mapped))
            return mapped;

        return string.IsNullOrWhiteSpace(browserContentType) ? Fallback : browserContentType.Trim();
    }
}
