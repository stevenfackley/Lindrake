using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lindrake.Api.Data.Migrations
{
    /// <summary>
    /// Makes the <c>InitialCreate</c> tenant policies on <c>data_sources</c>,
    /// <c>document_chunks</c> and <c>query_history</c> fail closed the way the
    /// <c>audit_log</c> ones already do. They cast <c>current_setting('app.current_tenant', true)</c>
    /// straight to <c>uuid</c>: NULL on a session that never set it (no rows, fine), but
    /// <c>''</c> once Npgsql's <c>DISCARD ALL</c> has reset a pooled connection a tenant
    /// used before, and <c>''::uuid</c> fails with 22P02. An unbound scope (startup,
    /// health checks, HS256 register) touching those tables on such a connection got an
    /// error instead of no rows. Same predicate as <c>AddAuditLogRowLevelSecurity</c>:
    /// <c>NULLIF(..., '')</c>, in a sub-SELECT so it is evaluated once per statement.
    /// Found by the Postgres integration suite; no model change.
    /// </summary>
    public partial class FailClosedTenantPolicies : Migration
    {
        private const string SessionTenant = "(SELECT NULLIF(current_setting('app.current_tenant', true), '')::uuid)";

        private const string LegacySessionTenant = "current_setting('app.current_tenant', true)::uuid";

        private static readonly string[] Tables = ["data_sources", "document_chunks", "query_history"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(AlterPolicies(SessionTenant));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(AlterPolicies(LegacySessionTenant));

        private static string AlterPolicies(string sessionTenant) => string.Join("\n", Tables.Select(table => $"""
            ALTER POLICY tenant_isolation_{table} ON {table}
                USING (tenant_id = {sessionTenant})
                WITH CHECK (tenant_id = {sessionTenant});
            """));
    }
}
