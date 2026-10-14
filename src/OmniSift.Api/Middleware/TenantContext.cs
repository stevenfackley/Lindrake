// ============================================================
// OmniSift.Api — Tenant Context Accessor
// Provides access to the current tenant ID from HttpContext
// ============================================================

using OmniSift.Api.Infrastructure;

namespace OmniSift.Api.Middleware;

/// <summary>
/// Interface for accessing the current tenant context.
/// Registered as scoped to ensure per-request isolation.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant ID for the request.
    /// </summary>
    Guid TenantId { get; }
}

/// <summary>
/// Resolves the current tenant ID from HttpContext items,
/// which is set by the TenantMiddleware.
/// </summary>
public sealed class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    /// <inheritdoc />
    public Guid TenantId
    {
        get
        {
            var context = httpContextAccessor.HttpContext
                ?? throw new TenantContextMissingException();

            if (context.Items.TryGetValue("TenantId", out var tenantIdObj) &&
                tenantIdObj is Guid tenantId)
            {
                return tenantId;
            }

            throw new TenantContextMissingException();
        }
    }
}
