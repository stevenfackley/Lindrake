// ============================================================
// Lindrake.Api — Vector Search Plugin for Semantic Kernel
// Thin adapter: runs IHybridSearchService, records citations,
// formats the hits for the LLM.
// ============================================================

using System.ComponentModel;
using System.Text.Json;
using Lindrake.Api.Services;
using Lindrake.Api.Services.Search;
using Microsoft.SemanticKernel;

namespace Lindrake.Api.Plugins;

/// <summary>
/// Semantic Kernel plugin exposing hybrid (vector + keyword) search over the
/// tenant's document chunks. Results are also recorded in
/// <see cref="ICitationAccumulator"/>.
/// </summary>
public sealed class VectorSearchPlugin(
    IHybridSearchService searchService,
    ICitationAccumulator citations,
    ILogger<VectorSearchPlugin> logger)
{
    /// <summary>Returned to the LLM when the search fails. Never the exception text.</summary>
    internal const string SearchUnavailableMessage =
        "Document search is temporarily unavailable. Tell the user their documents could not be searched.";

    internal const string NoResultsMessage =
        "No sufficiently relevant documents found for this query. The tenant may not have uploaded any relevant documents yet.";

    private const int SnippetLength = 300;

    private static readonly JsonSerializerOptions ResultJson = new() { WriteIndented = true };

    [KernelFunction("SearchDocuments")]
    [Description("Searches the tenant's uploaded documents for information relevant to the query. " +
                 "Returns the most semantically similar text chunks with metadata including source file names and relevance scores.")]
    public async Task<string> SearchDocumentsAsync(
        [Description("The search query describing the information to find")] string query,
        [Description("Maximum number of results to return (default: 5)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "No query provided for document search.";

        topK = Math.Clamp(topK, 1, 20);

        // Query text is user content (often PII) — log its size, never the text.
        logger.LogDebug("VectorSearch: queryLength={QueryLength}, topK={TopK}", query.Length, topK);

        IReadOnlyList<SearchHit> hits;
        try
        {
            hits = await searchService.SearchAsync(query, topK, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Document search failed");
            return SearchUnavailableMessage;
        }

        if (hits.Count == 0)
            return NoResultsMessage;

        foreach (var hit in hits)
        {
            citations.AddDocumentChunk(
                dataSourceId: hit.DataSourceId,
                chunkId: hit.ChunkId,
                fileName: hit.FileName,
                sourceType: hit.SourceType,
                relevanceScore: hit.RelevanceScore,
                snippet: hit.Content.Length > SnippetLength ? hit.Content[..SnippetLength] + "…" : hit.Content);
        }

        var formatted = hits.Select((hit, i) => new
        {
            rank = i + 1,
            chunkId = hit.ChunkId,
            dataSourceId = hit.DataSourceId,
            fileName = hit.FileName ?? "unknown",
            sourceType = hit.SourceType,
            chunkIndex = hit.ChunkIndex,
            relevanceScore = hit.RelevanceScore,
            content = hit.Content
        });

        return JsonSerializer.Serialize(formatted, ResultJson);
    }
}
