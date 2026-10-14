// ============================================================
// Lindrake.Api — DataSource Entity
// Represents an uploaded/ingested data source
// ============================================================

namespace Lindrake.Api.Models;

/// <summary>
/// Tracks every uploaded or ingested data source for a tenant.
/// Supports PDF, SMS exports, and web page captures.
/// </summary>
public sealed class DataSource : ITenantOwned, ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the owning tenant. Required for RLS.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Type of data source: "pdf", "sms", or "web".
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Original file name for uploaded files.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Original URL for web-captured sources.
    /// </summary>
    public string? OriginalUrl { get; set; }

    /// <summary>
    /// Processing status. Stored as a lowercase string in the database via EF value conversion.
    /// </summary>
    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Flexible metadata stored as JSONB.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>PII types detected during ingestion. Null = not yet scanned.</summary>
    public List<string>? PiiFlags { get; set; }

    /// <summary>True if any PII was detected.</summary>
    public bool HasPii { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<DocumentChunk> Chunks { get; set; } = [];
}
