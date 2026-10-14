using Lindrake.Api.Data;
using Lindrake.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class AuditController(LindrakeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AuditLogDto>>> GetAuditLog(CancellationToken cancellationToken)
    {
        var entries = await dbContext.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                TenantId = a.TenantId,
                UserId = a.UserId,
                Action = a.Action,
                ResourceType = a.ResourceType,
                ResourceId = a.ResourceId,
                IpAddress = a.IpAddress,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(entries);
    }
}
