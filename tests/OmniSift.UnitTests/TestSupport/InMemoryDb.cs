// ============================================================
// Unit Tests — EF InMemory DbContext helper
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OmniSift.Api.Data;
using OmniSift.Api.Models;

namespace OmniSift.UnitTests.TestSupport;

/// <summary>
/// Creates <see cref="OmniSiftDbContext"/> instances over a named InMemory store.
/// Contexts created with the same name share data, so a second context reads only
/// what the first one actually saved.
/// <para>
/// NOTE: InMemory has no transactions — assert on persisted state (via a fresh
/// context), not on commit/rollback behaviour.
/// </para>
/// </summary>
internal static class InMemoryDb
{
    public static OmniSiftDbContext Create(string databaseName, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<OmniSiftDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .Options;

        return new InMemoryOmniSiftDbContext(options);
    }

    /// <summary>Ignores the pgvector column, which InMemory cannot map.</summary>
    private sealed class InMemoryOmniSiftDbContext(DbContextOptions<OmniSiftDbContext> options)
        : OmniSiftDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Ignore(e => e.Embedding);
        }
    }
}
