// ============================================================
// Lindrake.Api — Tenant Entity
// Root multi-tenancy entity
// ============================================================

namespace Lindrake.Api.Models;

/// <summary>
/// Represents an organizational tenant in the multi-tenant system.
/// Each tenant has isolated data via PostgreSQL Row-Level Security.
/// </summary>
public sealed class Tenant : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the tenant/organization.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-safe unique identifier for the tenant.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex digest of the tenant's API key.
    /// The raw key is never stored.
    /// </summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Whether the tenant account is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<DataSource> DataSources { get; set; } = [];
    public ICollection<DocumentChunk> DocumentChunks { get; set; } = [];
    public ICollection<QueryHistory> QueryHistories { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
    public ICollection<AppUser> Users { get; set; } = [];
}
