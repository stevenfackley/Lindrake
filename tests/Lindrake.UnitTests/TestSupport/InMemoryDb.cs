// ============================================================
// Unit Tests — EF InMemory DbContext helper
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;

namespace OmniSift.UnitTests.TestSupport;

/// <summary>
/// Creates <see cref="OmniSiftDbContext"/> instances over a named InMemory store.
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
    public static OmniSiftDbContext Create(string databaseName, params IInterceptor[] interceptors) =>
        Create(databaseName, new TenantContext(), interceptors);

    /// <summary>A context scoped to <paramref name="tenantId"/>.</summary>
    public static OmniSiftDbContext ForTenant(string databaseName, Guid tenantId, params IInterceptor[] interceptors)
    {
        var tenant = new TenantContext();
        tenant.Set(tenantId);
        return Create(databaseName, tenant, interceptors);
    }

    /// <summary>A context scoped to <paramref name="tenantContext"/>.</summary>
    public static OmniSiftDbContext Create(
        string databaseName, ITenantContext tenantContext, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<OmniSiftDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .Options;

        return new InMemoryOmniSiftDbContext(options, tenantContext);
    }

    /// <summary>Ignores the pgvector column, which InMemory cannot map.</summary>
    private sealed class InMemoryOmniSiftDbContext(
        DbContextOptions<OmniSiftDbContext> options, ITenantContext tenantContext)
        : OmniSiftDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Ignore(e => e.Embedding);
        }
    }
}
