// ============================================================
// OmniSift.Api — Ingestion Status Enum
// Type-safe replacement for "pending"/"processing"/"completed"/"failed" magic strings
// ============================================================

namespace OmniSift.Api.Models;

/// <summary>
/// Represents the processing status of a data source ingestion pipeline.
/// Stored in the database as a lowercase string via EF Core value conversion.
/// </summary>
public enum IngestionStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
