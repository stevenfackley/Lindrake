// ============================================================
// OmniSift.Shared — Entity Graph DTOs
// Response types for the Entity Timelines + Relationship Graph feature
// ============================================================

namespace OmniSift.Shared.DTOs;

public sealed record EntityNode
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty; // person|org|place|date|event
    public int Mentions { get; init; }
}

public sealed record EntityEdge
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Relationship { get; init; } = string.Empty;
}

public sealed record TimelineEntry
{
    public string Date { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public List<string> Entities { get; init; } = [];
}

public sealed record EntityGraphResponse
{
    public List<EntityNode> Nodes { get; init; } = [];
    public List<EntityEdge> Edges { get; init; } = [];
    public List<TimelineEntry> Timeline { get; init; } = [];
    public int ChunksAnalyzed { get; init; }
}
