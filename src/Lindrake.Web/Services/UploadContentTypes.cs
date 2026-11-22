// ============================================================
// Lindrake.Web — MIME type of an uploaded file
// ============================================================

using Lindrake.Shared.DTOs;

namespace Lindrake.Web.Services;

/// <summary>
/// The MIME type to send with an upload. Browsers report nothing (or something
/// generic) for many of the formats Lindrake ingests, so the extension decides
/// for those, from <see cref="UploadFormats"/> (the table the API resolves by).
/// </summary>
public static class UploadContentTypes
{
    /// <summary>Sent when nothing better is known; the API then resolves by extension.</summary>
    public const string Fallback = "application/octet-stream";

    /// <summary>
    /// The MIME type for <paramref name="fileName"/>: by extension for the ingestible
    /// formats, else <paramref name="browserContentType"/>, else <see cref="Fallback"/>.
    /// </summary>
    public static string ForFileName(string fileName, string? browserContentType = null)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension) && UploadFormats.MimeTypeByExtension.TryGetValue(extension, out var mapped))
            return mapped;

        return string.IsNullOrWhiteSpace(browserContentType) ? Fallback : browserContentType.Trim();
    }
}
