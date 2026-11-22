// ============================================================
// Lindrake.Api — AuditLog → AuditLogDto projection
// ============================================================

using System.Linq.Expressions;
using Lindrake.Api.Models;
using Lindrake.Shared.DTOs;

namespace Lindrake.Api.Data.Projections;

/// <summary>
/// The single <see cref="AuditLog"/> → <see cref="AuditLogDto"/> mapping, as an
/// expression EF translates. Both the audit endpoint and the tenant export use it,
/// so the trail a tenant downloads has the fields the audit page shows.
/// </summary>
public static class AuditLogProjections
{
    public static readonly Expression<Func<AuditLog, AuditLogDto>> ToDto = a => new AuditLogDto
    {
        Id = a.Id,
        TenantId = a.TenantId,
        UserId = a.UserId,
        Action = a.Action,
        ResourceType = a.ResourceType,
        ResourceId = a.ResourceId,
        IpAddress = a.IpAddress,
        CreatedAt = a.CreatedAt
    };
}
