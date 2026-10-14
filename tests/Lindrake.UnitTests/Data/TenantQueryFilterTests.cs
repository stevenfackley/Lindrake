// ============================================================
// Unit Tests — global tenant query filter
// Every ITenantOwned entity is filtered to the context's tenant;
// an unbound context fails closed (no rows, no exception).
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Lindrake.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.UnitTests.Data;

public sealed class TenantQueryFilterTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly string _databaseName = "tenant-filter-" + Guid.NewGuid();

    [Fact]
    public async Task UnboundContext_SeesNoTenantOwnedRows()
    {
        await SeedAsync();

        await using var db = InMemoryDb.Create(_databaseName);

        (await db.DataSources.CountAsync()).Should().Be(0);
        (await db.DocumentChunks.CountAsync()).Should().Be(0);
        (await db.QueryHistories.CountAsync()).Should().Be(0);
        (await db.AuditLogs.CountAsync()).Should().Be(0);
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TenantContext_SeesOnlyItsOwnRows()
    {
        await SeedAsync();

        await using var db = InMemoryDb.ForTenant(_databaseName, TenantA);

        (await db.DataSources.Select(d => d.TenantId).ToListAsync()).Should().Equal(TenantA);
        (await db.DocumentChunks.Select(c => c.TenantId).ToListAsync()).Should().Equal(TenantA);
        (await db.QueryHistories.Select(q => q.TenantId).ToListAsync()).Should().Equal(TenantA);
        (await db.AuditLogs.Select(a => a.TenantId).ToListAsync()).Should().Equal(TenantA);
        (await db.Users.Select(u => u.TenantId).ToListAsync()).Should().Equal(TenantA);
    }

    [Fact]
    public async Task IgnoreQueryFilters_SeesEveryTenant()
    {
        await SeedAsync();

        await using var db = InMemoryDb.ForTenant(_databaseName, TenantA);

        (await db.DataSources.IgnoreQueryFilters([LindrakeDbContext.TenantFilter]).CountAsync()).Should().Be(2);
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Filter_ReadsTheTenantAtQueryTime_NotWhenTheModelWasBuilt()
    {
        // The model (and its filter) is built once per context type and cached;
        // the tenant must still come from the executing context.
        await SeedAsync();

        await using (var first = InMemoryDb.ForTenant(_databaseName, TenantA))
            (await first.DataSources.SingleAsync()).TenantId.Should().Be(TenantA);

        await using var second = InMemoryDb.ForTenant(_databaseName, TenantB);
        (await second.DataSources.SingleAsync()).TenantId.Should().Be(TenantB);
    }

    [Fact]
    public async Task Filter_FollowsTheTenantBoundAfterTheContextWasCreated()
    {
        await SeedAsync();
        var tenant = new TenantContext();
        await using var db = InMemoryDb.Create(_databaseName, tenant);

        (await db.DataSources.CountAsync()).Should().Be(0);

        tenant.Set(TenantB);
        (await db.DataSources.SingleAsync()).TenantId.Should().Be(TenantB);
    }

    [Fact]
    public void Model_EveryTenantOwnedEntityHasTheTenantFilter()
    {
        using var db = InMemoryDb.Create(_databaseName);

        var tenantOwned = db.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .ToList();

        tenantOwned.Select(e => e.ClrType).Should().BeEquivalentTo(
            [typeof(DataSource), typeof(DocumentChunk), typeof(QueryHistory), typeof(AuditLog), typeof(AppUser)]);
        tenantOwned.Should().OnlyContain(e => e.FindDeclaredQueryFilter(LindrakeDbContext.TenantFilter) != null);
    }

    /// <summary>One row of every tenant-owned entity for each of tenants A and B.</summary>
    private async Task SeedAsync()
    {
        await using var db = InMemoryDb.Create(_databaseName);

        foreach (var tenantId in new[] { TenantA, TenantB })
        {
            var source = new DataSource { TenantId = tenantId, SourceType = "pdf", FileName = $"{tenantId}.pdf" };
            db.DataSources.Add(source);
            db.DocumentChunks.Add(new DocumentChunk { TenantId = tenantId, DataSourceId = source.Id, Content = "text" });
            db.QueryHistories.Add(new QueryHistory { TenantId = tenantId, QueryText = "q" });
            db.AuditLogs.Add(new AuditLog { TenantId = tenantId, Action = "upload", ResourceType = "data_source" });
            db.Users.Add(new AppUser { TenantId = tenantId, Email = $"{tenantId}@example.test", PasswordHash = "x" });
        }

        await db.SaveChangesAsync();
    }
}
