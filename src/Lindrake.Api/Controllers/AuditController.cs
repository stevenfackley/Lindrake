using Lindrake.Api.Data;
using Lindrake.Api.Data.Projections;
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
            .Select(AuditLogProjections.ToDto)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(entries);
    }
}
