// ============================================================
// Unit Tests — fail-closed tenant policies migration
// Pins the SQL shape; the Postgres integration suite proves the
// behavior (no rows instead of 22P02 after a pool reset).
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Data.Migrations;
using Lindrake.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Lindrake.UnitTests.Data;

public sealed class FailClosedTenantPoliciesMigrationTests
{
    private const string SessionTenant = "(SELECT NULLIF(current_setting('app.current_tenant', true), '')::uuid)";

    [Theory]
    [InlineData("data_sources")]
    [InlineData("document_chunks")]
    [InlineData("query_history")]
    public void Up_ReadsTheTenantThroughNullIf_ForReadsAndWrites(string table)
    {
        var sql = Sql(new FailClosedTenantPolicies().UpOperations);

        sql.Should().Contain(
            $"ALTER POLICY tenant_isolation_{table} ON {table}\n" +
            $"    USING (tenant_id = {SessionTenant})\n" +
            $"    WITH CHECK (tenant_id = {SessionTenant});");
    }

    [Fact]
    public void Down_RestoresTheDirectCast()
    {
        var sql = Sql(new FailClosedTenantPolicies().DownOperations);

        sql.Should().Contain("USING (tenant_id = current_setting('app.current_tenant', true)::uuid)")
            .And.NotContain("NULLIF");
    }

    [Fact]
    public void Migration_IsDiscoveredAfterAddAuditLogRowLevelSecurity()
    {
        using var db = new LindrakeDbContext(new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=lindrake", npgsql => npgsql.UseVector())
            .Options, new TenantContext());

        db.Database.GetMigrations().Should()
            .ContainInOrder("20261014062417_AddAuditLogRowLevelSecurity", "20261014180000_FailClosedTenantPolicies");
    }

    private static string Sql(IEnumerable<MigrationOperation> operations) =>
        string.Join("\n", operations.OfType<SqlOperation>().Select(o => o.Sql))
            .ReplaceLineEndings("\n");
}
