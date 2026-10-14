// ============================================================
// Lindrake.Api — Ingestion Work Item
// Carries everything a background worker needs to run the
// extract→chunk→embed pipeline outside an HTTP request.
// The tenant ID is captured here because HttpContext is gone
// by the time the worker runs; the worker binds its scope's
// ITenantContext to it before touching the database.
// ============================================================

namespace Lindrake.Api.Services;

/// <summary>
/// Snapshot of an upload queued for asynchronous ingestion.
/// File bytes are buffered in-process so the HTTP request can
/// return a 202 immediately while the worker processes them.
/// </summary>
public sealed record IngestionWorkItem(
    /// <summary>DataSource row already created at 'Pending' status.</summary>
    Guid DataSourceId,
    /// <summary>Tenant that owns this data source. Worker uses this to set RLS.</summary>
    Guid TenantId,
    /// <summary>Source-type key used to select the correct ITextExtractor.</summary>
    string SourceType,
    /// <summary>Original upload file name (may be null for web ingestion).</summary>
    string? FileName,
    /// <summary>Buffered file content.</summary>
    byte[] Content);
