// ============================================================
// OmniSift.Api — Tenant-owned entity marker
// ============================================================

namespace OmniSift.Api.Models;

/// <summary>
/// An entity that belongs to exactly one tenant. <see cref="Data.OmniSiftDbContext"/>
/// applies a global query filter to every implementation, so queries only ever
/// see the current scope's tenant (Row-Level Security is the second line).
/// </summary>
public interface ITenantOwned
{
    /// <summary>The owning tenant.</summary>
    Guid TenantId { get; }
}
