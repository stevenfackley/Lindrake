// ============================================================
// Unit Tests — DataSourceProjections
// Renders the list query with the Npgsql provider (no connection
// is opened): the chunk count must be a COUNT subquery, never a
// load of the chunk rows (content + 384-float embedding).
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Data.Projections;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Lindrake.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.UnitTests.Data;

public sealed class DataSourceProjectionsTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly LindrakeDbContext _npgsql = new(new DbContextOptionsBuilder<LindrakeDbContext>()
        .UseNpgsql("Host=unused.invalid;Database=lindrake", npgsql => npgsql.UseVector())
        .Options, Bound(TenantId));

    [Fact]
    public void ToDtoPage_CountsChunksInSql_WithoutSelectingChunkColumns()
    {
        var sql = _npgsql.DataSources.ToDtoPage(skip: 0, take: 200).ToQueryString();

        sql.Should().ContainEquivalentOf("count(*)", because: "the chunk count is a COUNT subquery:\n" + sql)
            .And.Contain("FROM document_chunks");
        sql.Should().NotContain(".content", because: "chunk text must not be loaded:\n" + sql)
            .And.NotContain(".embedding", because: "chunk vectors must not be loaded:\n" + sql);
    }

    [Fact]
    public void ToDtoPage_OrdersNewestFirstAndPages()
    {
        var sql = _npgsql.DataSources.ToDtoPage(skip: 40, take: 20).ToQueryString();

        sql.Should().MatchRegex(@"ORDER BY \w+\.created_at DESC, \w+\.id")
            .And.Contain("LIMIT").And.Contain("OFFSET");
    }

    [Fact]
    public async Task ToDto_MapsEveryField_AndCountsOnlyTheSourcesChunks()
    {
        var databaseName = "projection-" + Guid.NewGuid();
        var source = new DataSource
        {
            TenantId = TenantId,
            SourceType = "pdf",
            FileName = "a.pdf",
            OriginalUrl = "https://example.test/a",
            Status = IngestionStatus.Failed,
            ErrorMessage = "boom",
            HasPii = true,
            PiiFlags = ["Ssn"]
        };
        var other = new DataSource { TenantId = TenantId, SourceType = "pdf" };

        await using (var seed = InMemoryDb.ForTenant(databaseName, TenantId))
        {
            seed.DataSources.AddRange(source, other);
            seed.DocumentChunks.AddRange(
                new DocumentChunk { TenantId = TenantId, DataSourceId = source.Id, Content = "1" },
                new DocumentChunk { TenantId = TenantId, DataSourceId = source.Id, Content = "2" },
                new DocumentChunk { TenantId = TenantId, DataSourceId = other.Id, Content = "3" });
            await seed.SaveChangesAsync();
        }

        await using var db = InMemoryDb.ForTenant(databaseName, TenantId);
        var dto = await db.DataSources.Where(d => d.Id == source.Id).Select(DataSourceProjections.ToDto).SingleAsync();

        dto.Id.Should().Be(source.Id);
        dto.SourceType.Should().Be("pdf");
        dto.FileName.Should().Be("a.pdf");
        dto.OriginalUrl.Should().Be("https://example.test/a");
        dto.Status.Should().Be("failed");
        dto.ErrorMessage.Should().Be("boom");
        dto.ChunkCount.Should().Be(2);
        dto.HasPii.Should().BeTrue();
        dto.PiiFlags.Should().Equal("Ssn");
    }

    public void Dispose() => _npgsql.Dispose();

    private static TenantContext Bound(Guid tenantId)
    {
        var tenant = new TenantContext();
        tenant.Set(tenantId);
        return tenant;
    }
}
