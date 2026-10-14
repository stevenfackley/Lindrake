// ============================================================
// Unit Tests — HybridSearchService SQL shape
// The arms need PostgreSQL (pgvector + pg_trgm) to execute, so these
// tests render the SQL with the Npgsql provider — no connection is
// opened — and assert on its shape: an explicit tenant predicate in
// both arms (RLS is the second line of defence, not the first), and
// a keyword arm that is independent of the vector candidates.
// ============================================================

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Services;
using OmniSift.Api.Services.Search;
using Pgvector;

namespace OmniSift.UnitTests.Services.Search;

public sealed partial class HybridSearchServiceTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Vector Embedding = new(new float[384]);

    private readonly TenantContext _tenant = BoundTenant();
    private readonly OmniSiftDbContext _db;

    public HybridSearchServiceTests() => _db = new(new DbContextOptionsBuilder<OmniSiftDbContext>()
        .UseNpgsql("Host=unused.invalid;Database=omnisift", npgsql => npgsql.UseVector())
        .Options, _tenant);

    [Fact]
    public void VectorArm_FiltersOnTenantAndOrdersByCosineDistance()
    {
        var sql = Service().VectorCandidates(TenantId, Embedding, 15).ToQueryString();

        TenantPredicate().IsMatch(sql).Should().BeTrue(because: "the vector arm must filter on tenant_id:\n" + sql);
        sql.Should().Contain("<=>").And.Contain("LIMIT");
    }

    [Fact]
    public void KeywordArm_FiltersOnTenantAndRanksByWordSimilarity()
    {
        var sql = Service().KeywordCandidates(TenantId, "John Smith", Embedding, 15).ToQueryString();

        TenantPredicate().IsMatch(sql).Should().BeTrue(because: "the keyword arm must filter on tenant_id:\n" + sql);
        sql.Should().Contain("word_similarity(").And.Contain("LIMIT");
    }

    [Fact]
    public void KeywordArm_DoesNotDependOnVectorCandidates()
    {
        // The old arm was limited to "id = ANY(vector candidates)", so it could only
        // reorder vector hits and never add recall.
        var sql = Service().KeywordCandidates(TenantId, "John Smith", Embedding, 15).ToQueryString();

        sql.Should().NotContainEquivalentOf("ANY(");
    }

    [Fact]
    public async Task SearchAsync_RejectsBlankQueryAndNonPositiveTopK()
    {
        var service = Service();

        await service.Invoking(s => s.SearchAsync(" ", 5, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.SearchAsync("q", 0, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    public void Dispose() => _db.Dispose();

    private HybridSearchService Service() =>
        new(_db, _tenant, Mock.Of<IEmbeddingService>(), NullLogger<HybridSearchService>.Instance);

    private static TenantContext BoundTenant()
    {
        var tenant = new TenantContext();
        tenant.Set(TenantId);
        return tenant;
    }

    // The explicit predicate, e.g.  WHERE d.tenant_id = @tenantId. Matching the
    // parameter name keeps the global tenant filter (@ef_filter__…) from satisfying it.
    [GeneratedRegex(@"\.tenant_id\s*=\s*@tenantId\b")]
    private static partial Regex TenantPredicate();
}
