// ============================================================
// Lindrake.Api — Ingestion configuration options
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace Lindrake.Api.Options;

/// <summary>
/// Limits for content pulled into the ingestion pipeline.
/// Bound from the "Ingestion" configuration section.
/// </summary>
public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>
    /// Largest web page body (bytes) the web-ingestion fetcher buffers; larger
    /// responses are aborted. Default 10 MB.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxWebPageBytes { get; set; } = 10 * 1024 * 1024;
}
