// ============================================================
// OmniSift.Api — Rate limiter policy names
// ============================================================

namespace OmniSift.Api.Infrastructure;

/// <summary>Named rate-limiter policies referenced by <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>Token bucket partitioned by the authenticated tenant_id claim.</summary>
    public const string PerTenant = "per-tenant";
}
