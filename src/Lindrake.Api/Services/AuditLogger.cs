using OmniSift.Api.Data;
using OmniSift.Api.Extensions;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;

namespace OmniSift.Api.Services;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for the current tenant, attributed to the
/// authenticated user (JWT <c>sub</c>) and client IP when a request is in scope.
/// Saves immediately: callers persist their own change first, then audit it.
/// </summary>
public sealed class AuditLogger(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    IHttpContextAccessor httpContextAccessor) : IAuditLogger
{
    public async Task LogAsync(string action, string resourceType, Guid? resourceId = null, CancellationToken ct = default)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var entry = new AuditLog
        {
            TenantId = tenantContext.TenantId,
            UserId = httpContext?.User.TryGetUserId(),
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString()
        };

        dbContext.AuditLogs.Add(entry);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
