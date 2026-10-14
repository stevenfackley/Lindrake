// ============================================================
// OmniSift.Api — Tenant Resolution Middleware
// Derives the tenant from the authenticated JWT (tenant_id claim)
// and binds it to the request's ITenantContext.
// ============================================================

using OmniSift.Api.Extensions;

namespace OmniSift.Api.Middleware;

/// <summary>
/// Resolves the current tenant from the validated JWT's <c>tenant_id</c> claim and
/// binds it to the request's <see cref="ITenantContext"/>. That is all it does: the
/// EF tenant query filter and <see cref="Data.TenantConnectionInterceptor"/> (which
/// sets the RLS session variable whenever a connection opens) read the context, so
/// no connection is held for the request.
///
/// The tenant is taken ONLY from the authenticated identity — the previous
/// <c>X-Tenant-Id</c> request header is no longer trusted, closing the
/// tenant-spoofing hole where any caller could select an arbitrary tenant.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // Skip tenant resolution for anonymous endpoints (health, Swagger, auth).
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        if (path.StartsWith("/health") || path.StartsWith("/swagger") || path.StartsWith("/api/auth"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Unauthenticated requests to protected endpoints are already rejected by
        // the [Authorize] authorization middleware before reaching here. If an
        // anonymous endpoint flows through, just continue without a tenant context.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Tenant identity comes exclusively from the validated token.
        var tenantId = context.User.TryGetTenantId();
        if (tenantId is null)
        {
            logger.LogWarning("Authenticated request to {Path} has no valid tenant_id claim", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Token does not carry a valid tenant_id claim."
            }).ConfigureAwait(false);
            return;
        }

        tenantContext.Set(tenantId.Value);
        logger.LogDebug("Tenant context set to {TenantId}", tenantId.Value);

        await next(context).ConfigureAwait(false);
    }
}

/// <summary>
/// Extension methods for registering the TenantMiddleware.
/// </summary>
public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantMiddleware>();
    }
}
