// ============================================================
// OmniSift.Api — Tenant Resolution Middleware
// Derives the tenant from the authenticated JWT (tenant_id claim)
// and binds it to the request's ITenantContext.
// ============================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    internal const string MissingTenantDetail = "Token does not carry a valid tenant_id claim.";

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // Anonymous endpoints (health, /api/auth) need no tenant. Unauthenticated
        // requests to protected endpoints were already rejected by authorization;
        // anything else unauthenticated (e.g. an unmatched route) carries no tenant.
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Tenant identity comes exclusively from the validated token. Authorization
        // requires the claim to exist; this also rejects one that is not a GUID or
        // is the empty GUID (a validly signed token can still carry either).
        var tenantId = context.User.TryGetTenantId();
        if (tenantId is null)
        {
            logger.LogWarning("Authenticated request to {Path} has no valid tenant_id claim", context.Request.Path);
            await WriteForbiddenAsync(context).ConfigureAwait(false);
            return;
        }

        tenantContext.Set(tenantId.Value);
        logger.LogDebug("Tenant context set to {TenantId}", tenantId.Value);

        await next(context).ConfigureAwait(false);
    }

    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = MissingTenantDetail,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        var problemDetails = context.RequestServices?.GetService<IProblemDetailsService>();
        var written = problemDetails is not null && await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        }).ConfigureAwait(false);

        if (!written)
        {
            await context.Response.WriteAsJsonAsync(
                problem, options: null, contentType: "application/problem+json", context.RequestAborted)
                .ConfigureAwait(false);
        }
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
