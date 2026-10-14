// ============================================================
// OmniSift.Api — Entity Extraction Service
// Extracts entities, relationships, and timeline from document chunks
// using Semantic Kernel / Anthropic LLM
// ============================================================

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OmniSift.Api.Data;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Services;

public interface IEntityExtractionService
{
    Task<EntityGraphResponse> ExtractAsync(Guid tenantId, CancellationToken ct);
}

public sealed class EntityExtractionService(
    OmniSiftDbContext dbContext,
    Kernel kernel,
    ILogger<EntityExtractionService> logger) : IEntityExtractionService
{
    private const string SystemPrompt = """
        You are a named entity recognition system. Analyze the following document text and extract a knowledge graph.

        Return ONLY a valid JSON object with this exact structure (no markdown, no explanation):
        {
          "nodes": [
            {"id": "node-1", "label": "Person Name", "type": "person", "mentions": 3}
          ],
          "edges": [
            {"source": "node-1", "target": "node-2", "relationship": "works at"}
          ],
          "timeline": [
            {"date": "2023-01-15", "label": "Brief event description", "entities": ["node-1"]}
          ]
        }

        Rules:
        - node types: person, org, place, date, event
        - node ids must be stable strings like "node-1", "node-2", etc.
        - edges reference node ids
        - timeline entries must have ISO date strings (YYYY-MM-DD or YYYY-MM or YYYY)
        - timeline must be sorted ascending by date
        - include only significant entities (minimum 2 mentions unless pivotal)
        - maximum 30 nodes, 50 edges, 25 timeline entries
        """;

    public async Task<EntityGraphResponse> ExtractAsync(Guid tenantId, CancellationToken ct)
    {
        // Fail before touching the database when no LLM is configured (503).
        var chatService = kernel.GetChatCompletionOrThrow();

        var chunks = await dbContext.DocumentChunks
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(30)
            .Select(c => c.Content)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (chunks.Count == 0)
        {
            logger.LogInformation("No document chunks found for tenant {TenantId}; returning empty graph.", tenantId);
            return new EntityGraphResponse();
        }

        var combined = string.Concat(chunks.Select(c => c + "\n\n"));
        if (combined.Length > 16000)
            combined = combined[..16000];

        logger.LogInformation(
            "Extracting entities for tenant {TenantId} from {ChunkCount} chunks ({CharCount} chars).",
            tenantId, chunks.Count, combined.Length);

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(SystemPrompt);
        chatHistory.AddUserMessage(combined);

        var result = await chatService.GetChatMessageContentAsync(chatHistory, cancellationToken: ct)
            .ConfigureAwait(false);

        var raw = result.Content ?? string.Empty;
        var graph = ParseGraphJson(raw);

        // Sort timeline ascending after parsing
        var sortedTimeline = graph.Timeline.OrderBy(e => e.Date).ToList();

        return graph with
        {
            Timeline = sortedTimeline,
            ChunksAnalyzed = chunks.Count
        };
    }

    /// <summary>
    /// Parses raw LLM output into an <see cref="EntityGraphResponse"/>.
    /// Handles code fences, surrounding prose, missing fields, and malformed JSON.
    /// </summary>
    internal static EntityGraphResponse ParseGraphJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new EntityGraphResponse();

        try
        {
            var text = raw.Trim();

            // Strip ```json ... ``` fences
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0)
                    text = text[(firstNewline + 1)..];

                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                    text = text[..lastFence];

                text = text.Trim();
            }

            // Extract first { ... last }
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                return new EntityGraphResponse();

            text = text[start..(end + 1)];

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<RawGraph>(text, opts);
            if (parsed is null)
                return new EntityGraphResponse();

            var nodes = parsed.Nodes?.Select(n => new EntityNode
            {
                Id = n.Id ?? string.Empty,
                Label = n.Label ?? string.Empty,
                Type = n.Type ?? string.Empty,
                Mentions = n.Mentions
            }).ToList() ?? [];

            var edges = parsed.Edges?.Select(e => new EntityEdge
            {
                Source = e.Source ?? string.Empty,
                Target = e.Target ?? string.Empty,
                Relationship = e.Relationship ?? string.Empty
            }).ToList() ?? [];

            var timeline = parsed.Timeline?.Select(t => new TimelineEntry
            {
                Date = t.Date ?? string.Empty,
                Label = t.Label ?? string.Empty,
                Entities = t.Entities ?? []
            }).ToList() ?? [];

            var sorted = timeline.OrderBy(t => t.Date).ToList();

            return new EntityGraphResponse
            {
                Nodes = nodes,
                Edges = edges,
                Timeline = sorted
            };
        }
        catch (Exception)
        {
            return new EntityGraphResponse();
        }
    }

    // ── Private deserialization shapes ──────────────────────────

    private sealed class RawGraph
    {
        public List<RawNode>? Nodes { get; set; }
        public List<RawEdge>? Edges { get; set; }
        public List<RawTimeline>? Timeline { get; set; }
    }

    private sealed class RawNode
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Type { get; set; }
        public int Mentions { get; set; }
    }

    private sealed class RawEdge
    {
        public string? Source { get; set; }
        public string? Target { get; set; }
        public string? Relationship { get; set; }
    }

    private sealed class RawTimeline
    {
        public string? Date { get; set; }
        public string? Label { get; set; }
        public List<string>? Entities { get; set; }
    }
}
