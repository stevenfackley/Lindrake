// ============================================================
// Lindrake.Api — Ingestion source-type keys
// ============================================================

namespace Lindrake.Api.Services;

/// <summary>
/// Source-type keys, as returned by each <see cref="ITextExtractor.SourceType"/>.
/// Which of them are accepted is decided by
/// <see cref="Extraction.ITextExtractorRegistry"/>, not by this list.
/// </summary>
public static class SourceTypes
{
    public const string Pdf = "pdf";
    public const string Sms = "sms";
    public const string Web = "web";
    public const string Docx = "docx";
    public const string Email = "email";
    public const string Text = "text";
}
