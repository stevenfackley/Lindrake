// ============================================================
// OmniSift.Api — Ingestion source-type keys
// ============================================================

using System.Collections.Frozen;
using OmniSift.Api.Infrastructure;

namespace OmniSift.Api.Services;

/// <summary>
/// Source-type keys accepted by the ingestion pipeline. Each maps to a keyed
/// <see cref="ITextExtractor"/> registration.
/// </summary>
public static class SourceTypes
{
    public static readonly FrozenSet<string> All =
        new[] { "pdf", "sms", "web", "docx", "email" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Lower-cases <paramref name="sourceType"/> and returns it, or throws
    /// <see cref="UnsupportedSourceTypeException"/> when no extractor handles it.
    /// </summary>
    public static string Normalize(string sourceType)
    {
        var normalized = sourceType.ToLowerInvariant();
        return All.Contains(normalized)
            ? normalized
            : throw new UnsupportedSourceTypeException(sourceType, All.Order(StringComparer.Ordinal));
    }
}
