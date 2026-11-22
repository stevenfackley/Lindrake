// ============================================================
// Lindrake.Shared — Ingestible upload formats
// ============================================================

using System.Collections.Frozen;

namespace Lindrake.Shared.DTOs;

/// <summary>
/// The file extensions Lindrake ingests and the MIME type each one is uploaded
/// with. The Web client sends that MIME type (<c>UploadContentTypes</c>); the API
/// resolves a generic or missing MIME type by the same extension
/// (<c>TextExtractorRegistry</c>), so the two cannot disagree.
/// </summary>
public static class UploadFormats
{
    /// <summary>MIME type by file extension (with the leading dot, case-insensitive).</summary>
    public static IReadOnlyDictionary<string, string> MimeTypeByExtension { get; } =
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
}
