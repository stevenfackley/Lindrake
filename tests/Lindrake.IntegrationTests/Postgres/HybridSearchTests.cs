// ============================================================
// Integration Tests (Postgres) — hybrid search
// pgvector cosine arm + pg_trgm word_similarity arm, fused with
// RRF, on real SQL. The keyword arm swallows its own failures
// (vector-only fallback), so only a real database shows that it
// runs, including with the extensions in qavren-db's schema.
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Services;
using Lindrake.Api.Services.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgvector;

namespace Lindrake.IntegrationTests.Postgres;

public abstract class HybridSearchTests<TFixture>
    where TFixture : PostgresFixture
{
    private const string Query = "Ballykilcline";

    /// <summary>Close to the query's embedding, shares no words with it.</summary>
    private const string VectorOnlyText = "Harbour ledger listing the ships that sailed from Cork that spring.";

    /// <summary>Contains the query word, embedding orthogonal to the query's.</summary>
    private const string KeywordOnlyText = "Baptism entry for Bridget, townland of Ballykilcline, parish of Kilglass.";

    private readonly TFixture _fixture;

    protected HybridSearchTests(TFixture fixture) => _fixture = fixture;

    private CustomWebApplicationFactory Factory => _fixture.Factory;

    [PostgresFact]
    public async Task Search_ReturnsAVectorOnlyAndAKeywordOnlyHit_FromTheTenantOnly()
    {
        var tenantId = await PostgresSeed.TenantAsync(Factory);
        var (_, chunks) = await PostgresSeed.DataSourceAsync(Factory, tenantId, 3, index => index switch
        {
            0 => (VectorOnlyText, Axis(0)),
            1 => (KeywordOnlyText, Axis(1)),
            _ => ("Weather notes for the harvest season.", Axis(2)),
        });

        // Another tenant holds identical copies of both hits.
        var otherTenant = await PostgresSeed.TenantAsync(Factory);
        await PostgresSeed.DataSourceAsync(Factory, otherTenant, 2, index => index == 0
            ? (VectorOnlyText, Axis(0))
            : (KeywordOnlyText, Axis(1)));

        using var scope = Factory.CreateTenantScope(tenantId);
        var embeddings = new Mock<IEmbeddingService>();
        embeddings
            .Setup(e => e.GenerateEmbeddingAsync(Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Axis(0));
        var search = new HybridSearchService(
            scope.ServiceProvider.GetRequiredService<LindrakeDbContext>(),
            scope.ServiceProvider.GetRequiredService<ITenantContext>(),
            embeddings.Object,
            NullLogger<HybridSearchService>.Instance);

        var hits = await search.SearchAsync(Query, topK: 5, CancellationToken.None);

        hits.Select(h => h.ChunkId).Should().BeEquivalentTo([chunks[0], chunks[1]]);

        var vectorHit = hits.Single(h => h.ChunkId == chunks[0]);
        vectorHit.VectorSimilarity.Should().BeApproximately(1.0, 1e-6);
        vectorHit.KeywordSimilarity.Should().BeNull("the vector-only chunk does not contain the query word");

        var keywordHit = hits.Single(h => h.ChunkId == chunks[1]);
        keywordHit.KeywordSimilarity.Should().BeGreaterThanOrEqualTo(0.3, "the keyword arm's floor");
        keywordHit.VectorSimilarity.Should().BeApproximately(0.0, 1e-6, "the keyword arm reports the cosine score too");
    }

    /// <summary>A 384-dimension unit vector along <paramref name="axis"/>.</summary>
    private static Vector Axis(int axis)
    {
        var values = new float[384];
        values[axis] = 1f;
        return new Vector(values);
    }
}

/// <summary>Local compose layout: extensions in <c>public</c>.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PublicSchemaHybridSearchTests(PublicSchemaPostgresFixture fixture)
    : HybridSearchTests<PublicSchemaPostgresFixture>(fixture), IClassFixture<PublicSchemaPostgresFixture>;

/// <summary>qavren-db layout: extensions in <c>extensions</c>, reached through the role's search_path.</summary>
[Collection(PostgresCollection.Name)]
public sealed class QavrenSchemaHybridSearchTests(QavrenSchemaPostgresFixture fixture)
    : HybridSearchTests<QavrenSchemaPostgresFixture>(fixture), IClassFixture<QavrenSchemaPostgresFixture>;
