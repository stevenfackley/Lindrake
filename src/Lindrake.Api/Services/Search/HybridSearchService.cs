// ============================================================
// Lindrake.Api — Hybrid document search
// Vector arm (pgvector cosine) + keyword arm (pg_trgm
// word_similarity), each tenant-scoped and run independently,
// fused with Reciprocal Rank Fusion.
// ============================================================

using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Lindrake.Api.Services.Search;

/// <summary>Searches the current tenant's document chunks.</summary>
public interface IHybridSearchService
{
    /// <summary>
    /// The <paramref name="topK"/> best chunks for <paramref name="query"/>, best first.
    /// Empty when nothing is relevant enough.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int topK, CancellationToken cancellationToken);
}

/// <summary>A document chunk returned by <see cref="IHybridSearchService"/>.</summary>
/// <param name="VectorSimilarity">Cosine similarity to the query (null if the chunk has no embedding).</param>
/// <param name="KeywordSimilarity">pg_trgm word similarity, when the keyword arm found the chunk.</param>
/// <param name="FusedScore">RRF score the hits are ordered by.</param>
public sealed record SearchHit(
    Guid ChunkId,
    Guid DataSourceId,
    string? FileName,
    string SourceType,
    int ChunkIndex,
    string Content,
    double? VectorSimilarity,
    double? KeywordSimilarity,
    double FusedScore)
{
    /// <summary>
    /// The score shown to users and the LLM: cosine similarity when known
    /// (comparable across queries), otherwise the keyword similarity.
    /// </summary>
    public double RelevanceScore => Math.Round(VectorSimilarity ?? KeywordSimilarity ?? 0, 4);
}

/// <summary>EF Core / PostgreSQL implementation of <see cref="IHybridSearchService"/>.</summary>
public sealed class HybridSearchService(
    LindrakeDbContext dbContext,
    ITenantContext tenantContext,
    IEmbeddingService embeddingService,
    ILogger<HybridSearchService> logger) : IHybridSearchService
{
    /// <summary>
    /// Vector candidates below this cosine similarity are dropped before fusing —
    /// filters noise before it reaches the LLM context window.
    /// </summary>
    internal const double VectorRelevanceThreshold = 0.25;

    /// <summary>
    /// Keyword candidates below this word similarity are dropped. Without a floor the
    /// keyword arm would always return its top N, however unrelated. 0.3 is
    /// pg_trgm's own default similarity threshold.
    /// </summary>
    internal const double KeywordSimilarityThreshold = 0.3;

    /// <summary>Each arm fetches topK × this many candidates as re-ranking headroom.</summary>
    internal const int CandidateMultiplier = 3;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int topK, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        var tenantId = tenantContext.TenantId;
        var candidateCount = topK * CandidateMultiplier;
        var embedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);

        var vectorHits = (await VectorCandidates(tenantId, embedding, candidateCount)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => 1.0 - c.Distance >= VectorRelevanceThreshold)
            .ToList();

        var keywordHits = await KeywordArmAsync(tenantId, query, embedding, candidateCount, cancellationToken)
            .ConfigureAwait(false);

        // Keyword rows carry both scores; vector rows fill in the rest.
        var candidates = new Dictionary<Guid, ChunkCandidate>();
        foreach (var candidate in keywordHits.Concat(vectorHits))
            candidates.TryAdd(candidate.Id, candidate);

        var fused = ReciprocalRankFusion.Fuse(
            [.. vectorHits.Select(c => c.Id)],
            [.. keywordHits.Select(c => c.Id)]);

        logger.LogInformation(
            "Hybrid search for tenant {TenantId}: {VectorCount} vector + {KeywordCount} keyword candidates, {FusedCount} fused",
            tenantId, vectorHits.Count, keywordHits.Count, fused.Count);

        return [.. fused.Take(topK).Select(f => candidates[f.Key].ToHit(f.Score))];
    }

    /// <summary>Vector arm: the tenant's chunks nearest to the query embedding.</summary>
    internal IQueryable<ChunkCandidate> VectorCandidates(Guid tenantId, Vector embedding, int count) =>
        dbContext.DocumentChunks
            .Where(c => c.TenantId == tenantId && c.Embedding != null)
            .OrderBy(c => c.Embedding!.CosineDistance(embedding))
            .Take(count)
            .Select(c => new ChunkCandidate(
                c.Id,
                c.DataSourceId,
                c.DataSource.FileName,
                c.DataSource.SourceType,
                c.ChunkIndex,
                c.Content,
                c.Embedding!.CosineDistance(embedding),
                null));

    /// <summary>
    /// Keyword arm: the tenant's chunks with the highest trigram word similarity to
    /// the query, searched across all of the tenant's chunks — not only the vector
    /// candidates — so it can surface exact names and terms embeddings miss.
    /// </summary>
    internal IQueryable<ChunkCandidate> KeywordCandidates(Guid tenantId, string query, Vector embedding, int count) =>
        dbContext.DocumentChunks
            .Where(c => c.TenantId == tenantId
                && EF.Functions.TrigramsWordSimilarity(query, c.Content) >= KeywordSimilarityThreshold)
            .OrderByDescending(c => EF.Functions.TrigramsWordSimilarity(query, c.Content))
            .Take(count)
            .Select(c => new ChunkCandidate(
                c.Id,
                c.DataSourceId,
                c.DataSource.FileName,
                c.DataSource.SourceType,
                c.ChunkIndex,
                c.Content,
                c.Embedding != null ? (double?)c.Embedding.CosineDistance(embedding) : null,
                EF.Functions.TrigramsWordSimilarity(query, c.Content)));

    private async Task<List<ChunkCandidate>> KeywordArmAsync(
        Guid tenantId, string query, Vector embedding, int count, CancellationToken cancellationToken)
    {
        try
        {
            return await KeywordCandidates(tenantId, query, embedding, count)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // pg_trgm missing or the query failed: degrade to vector-only ranking.
            logger.LogWarning(ex,
                "Keyword search arm failed for tenant {TenantId}; falling back to vector-only ranking",
                tenantId);
            return [];
        }
    }

    /// <summary>One row from either arm.</summary>
    internal sealed record ChunkCandidate(
        Guid Id,
        Guid DataSourceId,
        string? FileName,
        string SourceType,
        int ChunkIndex,
        string Content,
        double? Distance,
        double? WordSimilarity)
    {
        public SearchHit ToHit(double fusedScore) => new(
            Id, DataSourceId, FileName, SourceType, ChunkIndex, Content,
            VectorSimilarity: Distance is { } d ? 1.0 - d : null,
            KeywordSimilarity: WordSimilarity,
            FusedScore: fusedScore);
    }
}
