// ============================================================
// Unit Tests — audit_log Row-Level Security migration
// No database here, so this pins the migration's SQL shape.
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Data.Migrations;
using Lindrake.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Lindrake.UnitTests.Data;

public sealed class AuditLogRowLevelSecurityMigrationTests
{
    private const string SessionTenant = "(SELECT NULLIF(current_setting('app.current_tenant', true), '')::uuid)";

    [Fact]
    public void Up_EnablesAndForcesRls_WithTenantPoliciesAndNoUpdate()
    {
        var sql = Sql(new AddAuditLogRowLevelSecurity().UpOperations);

        sql.Should().Contain("ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY")
            .And.Contain("ALTER TABLE audit_log FORCE ROW LEVEL SECURITY");
        sql.Should().Contain($"FOR SELECT\n    USING (tenant_id = {SessionTenant})")
            .And.Contain($"FOR INSERT\n    WITH CHECK (tenant_id = {SessionTenant})")
            .And.Contain($"FOR DELETE\n    USING (tenant_id = {SessionTenant})");
        sql.Should().NotContain("FOR UPDATE").And.NotContain("FOR ALL", because: "the audit trail is append-only");
        sql.Should().NotContain(" users", because: "users stays without RLS (HS256 register needs a cross-tenant email check)");
    }

    [Fact]
    public void Down_DropsThePoliciesAndDisablesRls()
    {
        var sql = Sql(new AddAuditLogRowLevelSecurity().DownOperations);

        sql.Should().Contain("DROP POLICY IF EXISTS tenant_isolation_audit_log_select ON audit_log")
            .And.Contain("DROP POLICY IF EXISTS tenant_isolation_audit_log_insert ON audit_log")
            .And.Contain("DROP POLICY IF EXISTS tenant_isolation_audit_log_delete ON audit_log")
            .And.Contain("ALTER TABLE audit_log DISABLE ROW LEVEL SECURITY");
    }

    [Fact]
    public void Migration_IsDiscoveredAfterAddDataSourcePiiFlags()
    {
        using var db = new LindrakeDbContext(new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=lindrake", npgsql => npgsql.UseVector())
            .Options, new TenantContext());

        db.Database.GetMigrations().Should()
            .ContainInOrder("20260621060000_AddDataSourcePiiFlags", "20261014062417_AddAuditLogRowLevelSecurity");
    }

    /// <summary>The raw SQL of the operations, with line endings and indentation normalised.</summary>
    private static string Sql(IEnumerable<MigrationOperation> operations) =>
        string.Join("\n", operations.OfType<SqlOperation>().Select(o => o.Sql))
            .ReplaceLineEndings("\n");
}
