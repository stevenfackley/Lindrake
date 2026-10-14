using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniSift.Api.Data.Migrations
{
    /// <summary>
    /// Row-Level Security for <c>audit_log</c>, which <c>AddAuditLogAndUsers</c> created
    /// without any. Same <c>app.current_tenant</c> session-variable pattern as the
    /// InitialCreate tables, as one policy per command: SELECT, INSERT and DELETE are
    /// scoped to the session tenant; there is no UPDATE policy, so under FORCE the
    /// audit trail is append-only even for the table owner. DELETE stays allowed
    /// because the account-data erase (DELETE /api/account/data) removes the tenant's
    /// audit rows. The setting is read through <c>NULLIF(..., '')</c> so a connection
    /// with no tenant bound (unset, or reset to '' on pool reuse) matches no rows
    /// instead of failing the <c>::uuid</c> cast, and wrapped in a sub-SELECT so it is
    /// evaluated once per statement rather than per row (tenant_id is indexed).
    /// <para>
    /// <c>users</c> is deliberately left without RLS. The HS256 login now binds the
    /// request's tenant before its lookup, but HS256 register must check that an
    /// email is unused across ALL tenants (the email index is globally unique) before
    /// any tenant exists: under FORCE RLS that check would see no rows and a
    /// duplicate email would surface as a unique-violation 500 instead of a 409.
    /// <c>users</c> only backs the in-house HS256 IdP. That is not development-only: it
    /// is the default in Development/Testing and runs in any other environment with
    /// <c>Jwt:AllowLocalIdentityProvider=true</c> (DECISIONS.md, 2026-10-14), so a
    /// production deployment may hold users here. With <c>Supabase:Url</c> set the
    /// table stays empty and identities live in Supabase.
    /// </para>
    /// </summary>
    public partial class AddAuditLogRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
                ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_audit_log_select ON audit_log
                    FOR SELECT
                    USING (tenant_id = (SELECT NULLIF(current_setting('app.current_tenant', true), '')::uuid));

                CREATE POLICY tenant_isolation_audit_log_insert ON audit_log
                    FOR INSERT
                    WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.current_tenant', true), '')::uuid));

                CREATE POLICY tenant_isolation_audit_log_delete ON audit_log
                    FOR DELETE
                    USING (tenant_id = (SELECT NULLIF(current_setting('app.current_tenant', true), '')::uuid));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation_audit_log_delete ON audit_log;
                DROP POLICY IF EXISTS tenant_isolation_audit_log_insert ON audit_log;
                DROP POLICY IF EXISTS tenant_isolation_audit_log_select ON audit_log;

                ALTER TABLE audit_log NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE audit_log DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
