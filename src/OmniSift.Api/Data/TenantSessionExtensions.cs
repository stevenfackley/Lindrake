// ============================================================
// OmniSift.Api — PostgreSQL tenant session (RLS)
// ============================================================

using System.Data;
using Microsoft.EntityFrameworkCore;

namespace OmniSift.Api.Data;

public static class TenantSessionExtensions
{
    /// <summary>
    /// Opens the context's connection and sets <c>app.current_tenant</c> on it so
    /// Row-Level Security scopes every later query on this context to
    /// <paramref name="tenantId"/>. No-op for non-relational providers (InMemory).
    /// Used by TenantMiddleware (per request) and IngestionBackgroundService
    /// (per work item).
    /// </summary>
    public static async Task SetTenantSessionAsync(
        this OmniSiftDbContext dbContext,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (!dbContext.Database.IsRelational())
            return;

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            // set_config() rather than SET so the value can be a parameter.
            // 'false' = the setting lasts for the whole session/connection.
            command.CommandText = "SELECT set_config('app.current_tenant', @tenantId, false)";
            var param = command.CreateParameter();
            param.ParameterName = "@tenantId";
            param.Value = tenantId.ToString();
            command.Parameters.Add(param);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
