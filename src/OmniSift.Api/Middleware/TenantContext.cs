// ============================================================
// OmniSift.Api — Tenant Context
// The tenant a DI scope acts for: set by TenantMiddleware from the
// JWT claim, or by IngestionBackgroundService from the work item.
// ============================================================

using OmniSift.Api.Infrastructure;

namespace OmniSift.Api.Middleware;

/// <summary>
/// The tenant the current DI scope acts for. Registered as scoped, so each
/// HTTP request and each background work item gets its own instance.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The scope's tenant. Throws <see cref="TenantContextMissingException"/>
    /// when <see cref="Set"/> has not been called for this scope.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// The scope's tenant, or <see langword="null"/> when none is bound. Never
    /// throws: the EF tenant filter and the RLS connection interceptor read this
    /// so an unbound scope fails closed (sees no rows) instead of erroring.
    /// </summary>
    Guid? CurrentTenantId { get; }

    /// <summary>
    /// Binds the scope to <paramref name="tenantId"/>. A scope belongs to one
    /// tenant: re-binding it to a different tenant throws.
    /// </summary>
    void Set(Guid tenantId);
}

/// <summary>Default scoped <see cref="ITenantContext"/>.</summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    /// <inheritdoc />
    public Guid TenantId => _tenantId ?? throw new TenantContextMissingException();

    /// <inheritdoc />
    public Guid? CurrentTenantId => _tenantId;

    /// <inheritdoc />
    public void Set(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));

        if (_tenantId is { } current && current != tenantId)
            throw new InvalidOperationException("This scope is already bound to a different tenant.");

        _tenantId = tenantId;
    }
}
