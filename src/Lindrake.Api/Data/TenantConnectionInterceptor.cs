// ============================================================
// Lindrake.Api — PostgreSQL tenant session (RLS) on connection open
// ============================================================

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lindrake.Api.Data;

/// <summary>
/// Sets <c>app.current_tenant</c> — the session variable the Row-Level Security
/// policies read — every time EF opens a connection for an
/// <see cref="LindrakeDbContext"/> whose tenant is bound.
/// <para>
/// Hooking the open (rather than opening the connection up front per request)
/// means a connection is held only while EF actually needs it — not across LLM
/// calls or for requests that never touch the database — and a connection
/// re-opened by the retrying execution strategy is scoped again. Npgsql resets
/// session state when a pooled connection is reused, so the setting never leaks
/// to another scope. Unbound scopes (startup migrations, health checks, HS256
/// register) set nothing, and RLS then matches no tenant rows.
/// </para>
/// Stateless: one instance serves every context; the tenant comes from the
/// context the event belongs to. Never fires for non-relational providers.
/// </summary>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    // set_config() rather than SET so the value can be a parameter.
    // 'false' = the setting lasts for the whole session (connection), not one transaction.
    internal const string SetTenantSql = "SELECT set_config('app.current_tenant', @tenantId, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (TenantOf(eventData) is not { } tenantId)
            return;

        using var command = CreateCommand(connection, tenantId);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (TenantOf(eventData) is not { } tenantId)
            return;

        var command = CreateCommand(connection, tenantId);
        await using (command.ConfigureAwait(false))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Guid? TenantOf(ConnectionEndEventData eventData) =>
        (eventData?.Context as LindrakeDbContext)?.TenantContext.CurrentTenantId;

    private static DbCommand CreateCommand(DbConnection connection, Guid tenantId)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetTenantSql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tenantId";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);

        return command;
    }
}
