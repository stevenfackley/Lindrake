using System.Security.Claims;

namespace OmniSift.Api.Extensions;

/// <summary>
/// Reads the authenticated identity from the validated JWT. Tokens are validated
/// with <c>MapInboundClaims = false</c>, so claim names are the raw JWT names
/// ("sub", "tenant_id") rather than the legacy SOAP URIs.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>JWT claim carrying the tenant id.</summary>
    public const string TenantIdClaim = "tenant_id";

    /// <summary>
    /// The user id (JWT <c>sub</c>) — the Supabase auth user id, or the in-house
    /// <c>AppUser.Id</c> for legacy tokens. Null when missing or not a GUID.
    /// </summary>
    public static Guid? TryGetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// The tenant the token is scoped to (JWT <c>tenant_id</c> claim). This is the
    /// ONLY trusted source of tenant identity — the <c>X-Tenant-Id</c> request
    /// header is never used for authorization. Null when missing, not a GUID, or
    /// the empty GUID (no tenant can have that id, and <c>ITenantContext.Set</c>
    /// rejects it).
    /// </summary>
    public static Guid? TryGetTenantId(this ClaimsPrincipal user)
    {
        var tenant = user.FindFirst(TenantIdClaim)?.Value;
        return Guid.TryParse(tenant, out var id) && id != Guid.Empty ? id : null;
    }
}
