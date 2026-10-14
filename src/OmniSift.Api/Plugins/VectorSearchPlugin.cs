// ============================================================
// OmniSift.Api — Vector Search Plugin for Semantic Kernel
// Searches tenant document chunks using hybrid vector + keyword
// search with Reciprocal Rank Fusion (RRF).
// ============================================================

using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Services;
using Pgvector.EntityFrameworkCore;

namespace OmniSift.Api.Plugins;

/// <summary>
/// Semantic Kernel plugin that performs hybrid similarity search
/// (vector cosine + pg_trgm keyword) against the tenant's document
/// chunks, fused with Reciprocal Rank Fusion (RRF).
/// Also records results in <see cref="ICitationAccumulator"/>.
/// </summary>
public sealed class VectorSearchPlugin(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    IEmbeddingService embeddingService,
    ICitationAccumulator citations,
    ILogger<VectorSearchPlugin> logger)
{
    /// <summary>
    /// Chunks below this cosine-similarity threshold are dropped before
    /// fusing — filters noise before it reaches the LLM context window.
    /// </summary>
    private const double RelevanceThreshold = 0.25;

    [KernelFunction("SearchDocuments")]
    [Description("Searches the tenant's uploaded documents for information relevant to the query. " +
                 "Returns the most semantically similar text chunks with metadata including source file names and relevance scores.")]
    public async Task<string> SearchDocumentsAsync(
        [Description("The search query describing the information to find")] string query,
        [Description("Maximum number of results to return (default: 5)")] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "No query provided for document search.";

        topK = Math.Clamp(topK, 1, 20);

        // Query text is user content (often PII) — log its size, never the text.
        logger.LogDebug("VectorSearch: queryLength={QueryLength}, topK={TopK}, tenant={TenantId}",
            query.Length, topK, tenantContext.TenantId);

        try
        {
            // ── Vector arm ────────────────────────────────────────
            var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query).ConfigureAwait(false);

            // Retrieve up to topK * 3 candidates for re-ranking headroom
            var candidateCount = topK * 3;

            var vectorResults = await dbContext.DocumentChunks
                .Where(c => c.TenantId == tenantContext.TenantId)
                .Where(c => c.Embedding != null)
                .OrderBy(c => c.Embedding!.CosineDistance(queryEmbedding))
                .Take(candidateCount)
                .Select(c => new
                {
                    c.Id,
                    c.Content,
                    c.ChunkIndex,
                    c.DataSourceId,
                    DataSourceFileName = c.DataSource.FileName,
                    DataSourceType = c.DataSource.SourceType,
                    Distance = c.Embedding!.CosineDistance(queryEmbedding)
                })
                .ToListAsync().ConfigureAwait(false);

            if (vectorResults.Count == 0)
            {
                return "No relevant documents found for this query. The tenant may not have uploaded any documents yet.";
            }

            // Apply relevance threshold on the vector arm
            var aboveThreshold = vectorResults
                .Where(r => (1.0 - r.Distance) >= RelevanceThreshold)
                .ToList();

            if (aboveThreshold.Count == 0)
            {
                logger.LogInformation(
                    "VectorSearch: all {Count} candidates below threshold {Threshold} for tenant {TenantId}",
                    vectorResults.Count, RelevanceThreshold, tenantContext.TenantId);
                return "No sufficiently relevant documents found for this query.";
            }

            // ── Keyword arm (pg_trgm word_similarity) ─────────────
            // Pull the same candidate set and rank by trigram word-similarity.
            // We use EF FromSqlRaw with a parameterised query — safe from injection.
            var candidateIds = aboveThreshold.Select(r => r.Id).ToList();

            // Build a lookup from the vector results for later use
            var vectorLookup = aboveThreshold.ToDictionary(r => r.Id.ToString());

            // pg_trgm keyword ranking — returns ids ordered by similarity desc
            // We translate the LINQ-side similarity to a server-side expression.
            // EF FromSqlInterpolated prevents SQL injection.
            List<Guid> keywordRankedIds;
            try
            {
                keywordRankedIds = await dbContext.Database
                    .SqlQuery<Guid>(
                        $"""
                        SELECT id
                        FROM   document_chunks
                        WHERE  tenant_id = {tenantContext.TenantId}
                          AND  id = ANY({candidateIds.ToArray()})
                        ORDER  BY word_similarity({query}, content) DESC
                        """)
                    .ToListAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // pg_trgm may not be installed — fall back to vector-only ranking
                logger.LogWarning(ex,
                    "pg_trgm keyword ranking failed (extension may not be installed); " +
                    "falling back to vector-only ranking for tenant {TenantId}",
                    tenantContext.TenantId);
                keywordRankedIds = aboveThreshold.Select(r => r.Id).ToList();
            }

            // ── RRF fusion ────────────────────────────────────────
            var vectorRanking = aboveThreshold
                .Select((r, i) => new RankedItem(r.Id.ToString(), 1.0 - r.Distance))
                .ToList();

            var keywordRanking = keywordRankedIds
                .Select((id, i) => new RankedItem(id.ToString(), keywordRankedIds.Count - i))
                .ToList();

            var fusedKeys = ReciprocalRankFusion.Fuse(vectorRanking, keywordRanking);

            // Take topK from fused list, preserving order
            var finalIds = fusedKeys.Take(topK).ToHashSet(StringComparer.Ordinal);
            var orderedResults = fusedKeys
                .Take(topK)
                .Select(key => vectorLookup.TryGetValue(key, out var r) ? r : null)
                .Where(r => r is not null)
                .ToList();

            // ── Record citations ──────────────────────────────────
            foreach (var r in orderedResults)
            {
                if (r is null) continue;
                var score = Math.Round(1.0 - r.Distance, 4);
                var snippet = r.Content.Length > 300 ? r.Content[..300] + "…" : r.Content;
                citations.AddDocumentChunk(
                    dataSourceId: r.DataSourceId,
                    chunkId: r.Id,
                    fileName: r.DataSourceFileName,
                    sourceType: r.DataSourceType,
                    relevanceScore: score,
                    snippet: snippet);
            }

            var formattedResults = orderedResults
                .Where(r => r is not null)
                .Select((r, i) => new
                {
                    rank = i + 1,
                    chunkId = r!.Id,
                    dataSourceId = r.DataSourceId,
                    fileName = r.DataSourceFileName ?? "unknown",
                    sourceType = r.DataSourceType,
                    chunkIndex = r.ChunkIndex,
                    relevanceScore = Math.Round(1.0 - r.Distance, 4),
                    content = r.Content
                });

            logger.LogInformation(
                "VectorSearch returned {Count} hybrid results for query in tenant {TenantId}",
                orderedResults.Count, tenantContext.TenantId);

            return JsonSerializer.Serialize(formattedResults, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "VectorSearch failed for tenant {TenantId}", tenantContext.TenantId);
            return $"Error searching documents: {ex.Message}";
        }
    }
}
