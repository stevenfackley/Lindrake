using Lindrake.Api.Data;
using Lindrake.Api.Data.Projections;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Lindrake.Api.Services;
using Lindrake.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class AccountController(
    LindrakeDbContext dbContext,
    ITenantContext tenantContext,
    IAuditLogger auditLogger) : ControllerBase
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
            .Select(AuditLogProjections.ToDto)
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
            // No transaction on EF InMemory (tests): one SaveChanges (in LogAsync)
            // commits the removals and the audit entry together.
            await EraseTenantRowsAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await LogEraseAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Erases the tenant's rows and records the erase, committing both together.
    /// </summary>
    private async Task EraseInTransactionAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // A replayed attempt must not re-save the previous attempt's audit entry.
        dbContext.ChangeTracker.Clear();

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await EraseTenantRowsAsync(tenantId, cancellationToken).ConfigureAwait(false);
            await LogEraseAsync(tenantId, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The one list of what the erase empties: every tenant-owned table except
    /// <c>users</c> (the account itself stays), children before parents.
    /// </summary>
    private async Task EraseTenantRowsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await EraseTenantRowsAsync(dbContext.DocumentChunks, tenantId, cancellationToken).ConfigureAwait(false);
        await EraseTenantRowsAsync(dbContext.DataSources, tenantId, cancellationToken).ConfigureAwait(false);
        await EraseTenantRowsAsync(dbContext.QueryHistories, tenantId, cancellationToken).ConfigureAwait(false);
        await EraseTenantRowsAsync(dbContext.AuditLogs, tenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the tenant's rows of <paramref name="set"/>: set-based on a relational
    /// provider (no entity is loaded); EF InMemory has no ExecuteDelete, so there they
    /// are loaded and removed, and the caller's SaveChanges deletes them. Destructive,
    /// so the delete keeps an explicit tenant predicate on top of the query filter.
    /// </summary>
    private async Task EraseTenantRowsAsync<T>(DbSet<T> set, Guid tenantId, CancellationToken cancellationToken)
        where T : class, ITenantOwned
    {
        var rows = set.Where(e => e.TenantId == tenantId);

        if (dbContext.Database.IsRelational())
            await rows.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        else
            set.RemoveRange(await rows.ToListAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Records the erase. Written after the old audit rows are deleted, so the
    /// tenant's trail afterwards shows exactly that the erase happened.
    /// </summary>
    private Task LogEraseAsync(Guid tenantId, CancellationToken cancellationToken) =>
        auditLogger.LogAsync(AuditActions.DeleteAccountData, AuditResources.Account, tenantId, cancellationToken);
}
