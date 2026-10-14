// ============================================================
// OmniSift.Api — Tenant Resolution Middleware
// Derives the tenant from the authenticated JWT (tenant_id claim),
// binds it to ITenantContext and sets the PostgreSQL session
// variable for RLS enforcement.
// ============================================================

using OmniSift.Api.Data;
using OmniSift.Api.Extensions;

namespace OmniSift.Api.Middleware;

/// <summary>
/// Resolves the current tenant from the validated JWT's <c>tenant_id</c> claim,
/// binds it to the request's <see cref="ITenantContext"/>, and sets the
/// PostgreSQL session variable <c>app.current_tenant</c> for Row-Level Security.
///
/// The tenant is taken ONLY from the authenticated identity — the previous
/// <c>X-Tenant-Id</c> request header is no longer trusted, closing the
/// tenant-spoofing hole where any caller could select an arbitrary tenant.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, OmniSiftDbContext dbContext, ITenantContext tenantContext)
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

        // Scope every query on this request's connection to the tenant (RLS).
        try
        {
            await dbContext.SetTenantSessionAsync(tenantId.Value, context.RequestAborted).ConfigureAwait(false);
            logger.LogDebug("Tenant context set to {TenantId}", tenantId.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set tenant context for {TenantId}", tenantId.Value);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Failed to establish tenant context." }).ConfigureAwait(false);
            return;
        }

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
