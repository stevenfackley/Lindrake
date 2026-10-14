using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Data.Projections;
using OmniSift.Api.Middleware;
using OmniSift.Api.Services;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class AccountController(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    IAuditLogger auditLogger) : ControllerBase
{
    /// <summary>Audit action recorded by DELETE /api/account/data.</summary>
    internal const string EraseAuditAction = "delete_account_data";

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

        var tenantId = tenantContext.TenantId;

        if (dbContext.Database.IsRelational())
        {
            // Set-based deletes + the audit entry in one transaction; the retrying
            // execution strategy replays the whole unit on a transient failure.
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                ct => EraseInTransactionAsync(tenantId, ct), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // ExecuteDelete needs a relational provider (EF InMemory in tests): load
            // and remove instead. One SaveChanges (in LogAsync) commits both.
            dbContext.DocumentChunks.RemoveRange(await dbContext.DocumentChunks
                .Where(c => c.TenantId == tenantId).ToListAsync(cancellationToken).ConfigureAwait(false));
            dbContext.DataSources.RemoveRange(await dbContext.DataSources
                .Where(ds => ds.TenantId == tenantId).ToListAsync(cancellationToken).ConfigureAwait(false));
            dbContext.QueryHistories.RemoveRange(await dbContext.QueryHistories
                .Where(q => q.TenantId == tenantId).ToListAsync(cancellationToken).ConfigureAwait(false));
            dbContext.AuditLogs.RemoveRange(await dbContext.AuditLogs
                .Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken).ConfigureAwait(false));

            await LogEraseAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Deletes the tenant's rows set-based (no entity is loaded) and records the
    /// erase, committing both together. Destructive, so every delete keeps an
    /// explicit tenant predicate on top of the query filter.
    /// </summary>
    private async Task EraseInTransactionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // A replayed attempt must not re-save the previous attempt's audit entry.
        dbContext.ChangeTracker.Clear();

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await dbContext.DocumentChunks.Where(c => c.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.DataSources.Where(ds => ds.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.QueryHistories.Where(q => q.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await dbContext.AuditLogs.Where(a => a.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            await LogEraseAsync(tenantId, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records the erase. Written after the old audit rows are deleted, so the
    /// tenant's trail afterwards shows exactly that the erase happened.
    /// </summary>
    private Task LogEraseAsync(Guid tenantId, CancellationToken cancellationToken) =>
        auditLogger.LogAsync(EraseAuditAction, "account", tenantId, cancellationToken);
}
