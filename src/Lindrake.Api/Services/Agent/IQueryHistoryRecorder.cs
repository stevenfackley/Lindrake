// ============================================================
// Lindrake.Api — Query history persistence for agent runs
// ============================================================

using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;

namespace Lindrake.Api.Services.Agent;

/// <summary>Persists a finished agent run to the tenant's query history and audit log.</summary>
public interface IQueryHistoryRecorder
{
    /// <summary>
    /// Saves the run and its audit entry. Deliberately takes no request token: an
    /// answer that was produced must be recorded even if the client disconnects
    /// (a streaming client typically closes right after the final frame). The
    /// writes are bounded by their own short timeout instead.
    /// </summary>
    Task RecordAsync(string queryText, AgentRunResult result, string auditAction);
}

/// <summary>EF Core implementation over the request's <see cref="LindrakeDbContext"/>.</summary>
public sealed class QueryHistoryRecorder(
    LindrakeDbContext dbContext,
    ITenantContext tenantContext,
    IAuditLogger auditLogger) : IQueryHistoryRecorder
{
    /// <summary>Upper bound on the history + audit writes.</summary>
    internal static readonly TimeSpan PersistTimeout = TimeSpan.FromSeconds(10);

    public async Task RecordAsync(string queryText, AgentRunResult result, string auditAction)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var timeout = new CancellationTokenSource(PersistTimeout);

        var entry = new QueryHistory
        {
            TenantId = tenantContext.TenantId,
            QueryText = queryText,
            ResponseText = result.Response,
            PluginsUsed = [.. result.PluginsUsed],
            Sources = [.. result.Sources],
            DurationMs = result.DurationMs
        };

        dbContext.QueryHistories.Add(entry);
        await dbContext.SaveChangesAsync(timeout.Token).ConfigureAwait(false);

        await auditLogger.LogAsync(auditAction, "query_history", entry.Id, timeout.Token).ConfigureAwait(false);
    }
}
