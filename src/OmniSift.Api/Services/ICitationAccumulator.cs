// ============================================================
// OmniSift.Api — Citation Accumulator
// Scoped per-request collector that plugins write citations to.
// ResearchAgentService reads it after the agent finishes to populate
// Sources on the response.
// ============================================================

using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Services;

/// <summary>
/// Per-request, scoped accumulator that plugins write citations to.
/// </summary>
public interface ICitationAccumulator
{
    /// <summary>
    /// Record a document chunk citation from the vector-search plugin.
    /// </summary>
    void AddDocumentChunk(
        Guid dataSourceId,
        Guid chunkId,
        string? fileName,
        string? sourceType,
        double relevanceScore,
        string? snippet = null);

    /// <summary>
    /// Record a web result citation from the web-search plugin.
    /// </summary>
    void AddWebResult(string url, string title, double score, string? snippet = null);

    /// <summary>
    /// Record an archived-page citation from the Wayback Machine plugin.
    /// </summary>
    void AddArchivedPage(string archiveUrl, string originalUrl, string? timestamp);

    /// <summary>
    /// Return deduplicated citations ordered by relevance score descending.
    /// </summary>
    IReadOnlyList<SourceCitation> GetCitations();
}

/// <summary>
/// Default in-memory implementation, registered as Scoped.
/// Thread-safety is not required — each HTTP request gets its own instance.
/// </summary>
public sealed class CitationAccumulator : ICitationAccumulator
{
    // Keyed by a dedup key so we don't double-report the same chunk/URL
    private readonly Dictionary<string, SourceCitation> _citations = [];

    public void AddDocumentChunk(
        Guid dataSourceId,
        Guid chunkId,
        string? fileName,
        string? sourceType,
        double relevanceScore,
        string? snippet = null)
    {
        var key = $"doc:{chunkId}";
        // Keep whichever has the higher score (may be called twice if RRF boosts a chunk)
        if (_citations.TryGetValue(key, out var existing) &&
            (existing.RelevanceScore ?? 0) >= relevanceScore)
            return;

        _citations[key] = new SourceCitation
        {
            Type = "document",
            DataSourceId = dataSourceId,
            ChunkId = chunkId,
            Title = fileName,
            RelevanceScore = Math.Round(relevanceScore, 4),
            Snippet = snippet
        };
    }

    public void AddWebResult(string url, string title, double score, string? snippet = null)
    {
        var key = $"web:{url}";
        if (_citations.TryGetValue(key, out var existing) &&
            (existing.RelevanceScore ?? 0) >= score)
            return;

        _citations[key] = new SourceCitation
        {
            Type = "web",
            Url = url,
            Title = title,
            RelevanceScore = Math.Round(score, 4),
            Snippet = snippet
        };
    }

    public void AddArchivedPage(string archiveUrl, string originalUrl, string? timestamp)
    {
        var key = $"archive:{archiveUrl}";
        if (_citations.ContainsKey(key))
            return;

        _citations[key] = new SourceCitation
        {
            Type = "archive",
            Url = archiveUrl,
            Title = string.IsNullOrWhiteSpace(timestamp)
                ? $"Archived: {originalUrl}"
                : $"Archived: {originalUrl} ({timestamp})",
            RelevanceScore = null,
            Snippet = null
        };
    }

    public IReadOnlyList<SourceCitation> GetCitations() =>
        [.. _citations.Values
            .OrderByDescending(c => c.RelevanceScore ?? -1)];
}
