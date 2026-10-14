// ============================================================
// Lindrake.Api — Tenant-owned entity marker
// ============================================================

namespace Lindrake.Api.Models;

/// <summary>
/// An entity that belongs to exactly one tenant. <see cref="Data.LindrakeDbContext"/>
/// applies a global query filter to every implementation, so queries only ever
/// see the current scope's tenant (Row-Level Security is the second line).
/// </summary>
public interface ITenantOwned
{
    /// <summary>The owning tenant.</summary>
    Guid TenantId { get; }
}
