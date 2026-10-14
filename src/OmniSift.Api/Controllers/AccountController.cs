using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Data.Projections;
using OmniSift.Api.Middleware;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class AccountController(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext) : ControllerBase
{
    [HttpGet("export")]
    public async Task<ActionResult<TenantExportDto>> Export(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;

        var dataSources = await dbContext.DataSources
            .OrderByDescending(ds => ds.CreatedAt)
            .Select(DataSourceProjections.ToDto)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var queryHistory = await dbContext.QueryHistories
            .Select(q => new QueryHistoryDto
            {
                Id = q.Id,
                QueryText = q.QueryText,
                ResponseText = q.ResponseText,
                PluginsUsed = q.PluginsUsed,
                DurationMs = q.DurationMs,
                CreatedAt = q.CreatedAt
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var auditLog = await dbContext.AuditLogs
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

        return Ok(new TenantExportDto
        {
            TenantId = tenantId,
            DataSources = dataSources,
            QueryHistory = queryHistory,
            AuditLog = auditLog
        });
    }

    [HttpDelete("data")]
    public async Task<IActionResult> DeleteAccountData(
        [FromBody] DeleteAccountDataRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Confirm != "DELETE")
            return Problem(
                detail: "Confirm field must equal 'DELETE' (case-sensitive).",
                statusCode: StatusCodes.Status400BadRequest);

        // Destructive: keep an explicit tenant predicate on top of the query filter.
        var tenantId = tenantContext.TenantId;

        var dataSources = await dbContext.DataSources
            .Where(ds => ds.TenantId == tenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        dbContext.DataSources.RemoveRange(dataSources);

        var queryHistories = await dbContext.QueryHistories
            .Where(q => q.TenantId == tenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        dbContext.QueryHistories.RemoveRange(queryHistories);

        var auditLogs = await dbContext.AuditLogs
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        dbContext.AuditLogs.RemoveRange(auditLogs);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
