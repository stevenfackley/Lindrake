// ============================================================
// Integration Tests — Postgres suite seeding
// Rows are written through the API's own DbContext, so tenant
// rows pass RLS WITH CHECK under the real interceptor.
// ============================================================

using Lindrake.Api.Data;
using Lindrake.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;

namespace Lindrake.IntegrationTests.Postgres;

/// <summary>What <see cref="PostgresSeed.TenantWithDataAsync"/> wrote for one tenant.</summary>
internal sealed record SeededTenant(
    Guid TenantId,
    Guid DataSourceId,
    IReadOnlyList<Guid> ChunkIds,
    Guid QueryHistoryId,
    Guid AuditLogId);

internal static class PostgresSeed
{
    /// <summary>
    /// A <c>tenants</c> row (the foreign keys need one; the table has no RLS and no
    /// tenant filter, so an unbound scope writes it).
    /// </summary>
    public static async Task<Guid> TenantAsync(CustomWebApplicationFactory factory)
    {
        var id = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Name = "Tenant " + id.ToString("N")[..8],
            Slug = "t-" + id.ToString("N"),
            ApiKeyHash = id.ToString("N"),
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// A new tenant with one data source of <paramref name="chunkCount"/> chunks, one
    /// query-history row and one audit row.
    /// </summary>
    public static async Task<SeededTenant> TenantWithDataAsync(CustomWebApplicationFactory factory, int chunkCount = 2)
    {
        var tenantId = await TenantAsync(factory);
        var (dataSourceId, chunkIds) = await DataSourceAsync(factory, tenantId, chunkCount);

        using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        var query = new QueryHistory { TenantId = tenantId, QueryText = "Who emigrated in 1847?" };
        var audit = new AuditLog { TenantId = tenantId, Action = "upload", ResourceType = "data_source", ResourceId = dataSourceId };
        db.QueryHistories.Add(query);
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync();

        return new SeededTenant(tenantId, dataSourceId, chunkIds, query.Id, audit.Id);
    }

    /// <summary>
    /// One data source of <paramref name="chunkCount"/> chunks for an existing tenant.
    /// <paramref name="chunk"/> supplies each chunk's text and embedding (default:
    /// numbered text, no embedding).
    /// </summary>
    public static async Task<(Guid DataSourceId, IReadOnlyList<Guid> ChunkIds)> DataSourceAsync(
        CustomWebApplicationFactory factory,
        Guid tenantId,
        int chunkCount,
        Func<int, (string Content, Vector? Embedding)>? chunk = null)
    {
        chunk ??= index => ($"Chunk {index} of a parish register.", null);

        using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        var source = new DataSource { TenantId = tenantId, SourceType = "pdf", FileName = "register.pdf" };
        db.DataSources.Add(source);

        var chunks = Enumerable.Range(0, chunkCount).Select(index =>
        {
            var (content, embedding) = chunk(index);
            return new DocumentChunk
            {
                TenantId = tenantId,
                DataSourceId = source.Id,
                ChunkIndex = index,
                Content = content,
                Embedding = embedding,
            };
        }).ToList();
        db.DocumentChunks.AddRange(chunks);
        await db.SaveChangesAsync();

        return (source.Id, chunks.Select(c => c.Id).ToList());
    }
}
