// ============================================================
// Unit Tests — EF InMemory DbContext helper
// ============================================================

using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lindrake.UnitTests.TestSupport;

/// <summary>
/// Creates <see cref="LindrakeDbContext"/> instances over a named InMemory store.
/// Contexts created with the same name share data, so a second context reads only
/// what the first one actually saved.
/// <para>
/// The tenant query filter applies: a context with no tenant bound sees no
/// tenant-owned rows. Read back through <see cref="ForTenant"/>.
/// </para>
/// <para>
/// NOTE: InMemory has no transactions — assert on persisted state (via a fresh
/// context), not on commit/rollback behaviour.
/// </para>
/// </summary>
internal static class InMemoryDb
{
    /// <summary>A context with no tenant bound (sees no tenant-owned rows).</summary>
    public static LindrakeDbContext Create(string databaseName, params IInterceptor[] interceptors) =>
        Create(databaseName, new TenantContext(), interceptors);

    /// <summary>A context scoped to <paramref name="tenantId"/>.</summary>
    public static LindrakeDbContext ForTenant(string databaseName, Guid tenantId, params IInterceptor[] interceptors)
    {
        var tenant = new TenantContext();
        tenant.Set(tenantId);
        return Create(databaseName, tenant, interceptors);
    }

    /// <summary>A context scoped to <paramref name="tenantContext"/>.</summary>
    public static LindrakeDbContext Create(
        string databaseName, ITenantContext tenantContext, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .Options;

        return new InMemoryLindrakeDbContext(options, tenantContext);
    }

    /// <summary>Ignores the pgvector column, which InMemory cannot map.</summary>
    private sealed class InMemoryLindrakeDbContext(
        DbContextOptions<LindrakeDbContext> options, ITenantContext tenantContext)
        : LindrakeDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Ignore(e => e.Embedding);
        }
    }
}
