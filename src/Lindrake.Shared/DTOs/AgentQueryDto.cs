// ============================================================
// Lindrake.Shared — Agent Query DTOs
// Request/Response contracts for the AI research agent
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace Lindrake.Shared.DTOs;

/// <summary>
/// Request DTO for submitting a research query to the agent.
/// </summary>
public sealed record AgentQueryRequest
{
    /// <summary>
    /// The natural language research query.
    /// </summary>
    [Required]
    [MinLength(3, ErrorMessage = "Query must be at least 3 characters.")]
    [MaxLength(5000, ErrorMessage = "Query must not exceed 5000 characters.")]
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Optional conversation history for multi-turn context. Bounded so a client
    /// cannot inflate the LLM prompt (and its cost) without limit; the server
    /// additionally keeps only the most recent turns (Agent:MaxHistoryMessages).
    /// </summary>
    [MaxLength(MaxConversationTurns, ErrorMessage = "Conversation history must not exceed 50 messages.")]
    public List<ConversationMessage>? ConversationHistory { get; init; }

    /// <summary>Upper bound on <see cref="ConversationHistory"/> entries.</summary>
    public const int MaxConversationTurns = 50;
}

/// <summary>
/// A single message in the conversation history.
/// </summary>
public sealed record ConversationMessage
{
    /// <summary>Upper bound on <see cref="Content"/> length, in characters.</summary>
    public const int MaxContentLength = 32_000;

    [Required]
    [AllowedValues("user", "assistant", ErrorMessage = "Role must be 'user' or 'assistant'.")]
    public string Role { get; init; } = string.Empty;

    [Required]
    [MaxLength(MaxContentLength, ErrorMessage = "Message content must not exceed 32000 characters.")]
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// Response DTO from the AI research agent.
/// </summary>
public sealed record AgentQueryResponse
{
    /// <summary>
    /// The agent's synthesized response text.
    /// </summary>
    public string Response { get; init; } = string.Empty;

    /// <summary>
    /// Sources cited in the response.
    /// </summary>
    public List<SourceCitation> Sources { get; init; } = [];

    /// <summary>
    /// Names of Semantic Kernel plugins invoked during this query.
    /// </summary>
    public List<string> PluginsUsed { get; init; } = [];

    /// <summary>
    /// Processing duration in milliseconds.
    /// </summary>
    public int DurationMs { get; init; }
}

/// <summary>
/// SSE delta event emitted for each token chunk during streaming.
/// </summary>
public sealed record AgentStreamDeltaEvent
{
    public string Type { get; init; } = "delta";
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// SSE final event emitted at the end of a streaming response, carrying metadata.
/// </summary>
public sealed record AgentStreamFinalEvent
{
    public string Type { get; init; } = "final";
    public List<string> PluginsUsed { get; init; } = [];
    public int DurationMs { get; init; }
    public List<SourceCitation> Sources { get; init; } = [];
}

/// <summary>
/// SSE terminal event emitted when the stream fails after it has started. Carries a
/// generic, client-safe message; the underlying exception is only logged server-side.
/// </summary>
public sealed record AgentStreamErrorEvent
{
    public string Type { get; init; } = "error";
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// A citation to a source used in the agent's response.
/// </summary>
public sealed record SourceCitation
{
    public string Type { get; init; } = string.Empty; // "document", "web", "archive"
    public Guid? DataSourceId { get; init; }
    public Guid? ChunkId { get; init; }
    public string? Url { get; init; }
    public string? Title { get; init; }
    public double? RelevanceScore { get; init; }
    /// <summary>
    /// Short excerpt (≤ 300 chars) from the cited source. Optional.
    /// </summary>
    public string? Snippet { get; init; }
}
