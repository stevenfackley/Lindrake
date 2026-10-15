// ============================================================
// Integration Tests (Postgres) — tenant session on connection open
// With Database:MaxPoolSize=1 every scope reuses one physical
// connection, so each scope seeing only its own tenant proves the
// interceptor sets app.current_tenant on every open (after
// Npgsql's DISCARD ALL), not once per connection or process.
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lindrake.IntegrationTests.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class TenantConnectionInterceptorTests : IClassFixture<SingleConnectionPostgresFixture>
{
    private readonly SingleConnectionPostgresFixture _fixture;

    public TenantConnectionInterceptorTests(SingleConnectionPostgresFixture fixture) => _fixture = fixture;

    private CustomWebApplicationFactory Factory => _fixture.Factory;

    [PostgresFact]
    public async Task ScopesTakingTurnsOnOnePooledConnection_EachSeeOnlyTheirTenant()
    {
        var a = await PostgresSeed.TenantWithDataAsync(Factory, chunkCount: 1);
        var b = await PostgresSeed.TenantWithDataAsync(Factory, chunkCount: 3);

        var firstA = await ReadAsync(a.TenantId);
        var thenB = await ReadAsync(b.TenantId);
        var againA = await ReadAsync(a.TenantId);

        new[] { thenB.BackendPid, againA.BackendPid }.Should().AllBeEquivalentTo(firstA.BackendPid,
            "Database:MaxPoolSize=1 leaves the API a single physical connection");

        firstA.Setting.Should().Be(a.TenantId.ToString());
        firstA.ChunkTenants.Should().HaveCount(1).And.AllBeEquivalentTo(a.TenantId);
        thenB.Setting.Should().Be(b.TenantId.ToString());
        thenB.ChunkTenants.Should().HaveCount(3).And.AllBeEquivalentTo(b.TenantId);
        againA.ChunkTenants.Should().HaveCount(1).And.AllBeEquivalentTo(a.TenantId);
    }

    /// <summary>
    /// What one tenant-bound scope sees: its backend, the session setting and the
    /// tenant of every chunk visible to it with the EF tenant filter off, so RLS on
    /// the session setting is the only thing scoping the rows.
    /// </summary>
    private async Task<(int BackendPid, string Setting, List<Guid> ChunkTenants)> ReadAsync(Guid tenantId)
    {
        using var scope = Factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();

        var backendPid = await db.Database
            .SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var setting = await db.Database
            .SqlQuery<string>($"SELECT current_setting('app.current_tenant', true) AS \"Value\"").SingleAsync();

        // Ignores LindrakeDbContext.TenantFilter on purpose: this test is about the
        // database's own wall, which must hold without the EF filter.
        var chunkTenants = await db.DocumentChunks
            .IgnoreQueryFilters([LindrakeDbContext.TenantFilter])
            .Select(c => c.TenantId)
            .ToListAsync();

        return (backendPid, setting, chunkTenants);
    }
}
