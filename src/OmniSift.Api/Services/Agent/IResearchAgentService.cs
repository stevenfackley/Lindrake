// ============================================================
// OmniSift.Api — Research agent contract
// ============================================================

using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Services.Agent;

/// <summary>
/// Runs a research query through the Semantic Kernel agent, records it in the
/// tenant's query history and returns the answer with its plugins and sources.
/// </summary>
public interface IResearchAgentService
{
    /// <summary>Runs the query to completion.</summary>
    /// <exception cref="Infrastructure.LlmNotConfiguredException">No chat-completion service is configured.</exception>
    Task<AgentRunResult> RunAsync(AgentQueryRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Streams the answer as <see cref="AgentStreamEvent.Delta"/> chunks followed by a
    /// single <see cref="AgentStreamEvent.Completed"/>. LLM availability is checked
    /// eagerly, when this method is called, not on first enumeration.
    /// </summary>
    /// <exception cref="Infrastructure.LlmNotConfiguredException">No chat-completion service is configured.</exception>
    IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentQueryRequest request, CancellationToken cancellationToken);
}

/// <summary>Outcome of one agent run.</summary>
/// <param name="Response">The answer text (a fallback sentence when the model returned nothing).</param>
/// <param name="PluginsUsed">Kernel plugins the model invoked, in first-use order.</param>
/// <param name="DurationMs">Wall-clock time of the LLM run.</param>
/// <param name="Sources">Citations recorded by the plugins.</param>
public sealed record AgentRunResult(
    string Response,
    IReadOnlyList<string> PluginsUsed,
    int DurationMs,
    IReadOnlyList<SourceCitation> Sources)
{
    public AgentQueryResponse ToResponse() => new()
    {
        Response = Response,
        PluginsUsed = [.. PluginsUsed],
        DurationMs = DurationMs,
        Sources = [.. Sources]
    };

    public AgentStreamFinalEvent ToFinalEvent() => new()
    {
        Type = "final",
        PluginsUsed = [.. PluginsUsed],
        DurationMs = DurationMs,
        Sources = [.. Sources]
    };
}

/// <summary>An event in a streamed agent run.</summary>
public abstract record AgentStreamEvent
{
    private AgentStreamEvent() { }

    /// <summary>A chunk of answer text.</summary>
    public sealed record Delta(string Content) : AgentStreamEvent;

    /// <summary>The run finished and was recorded; always the last event.</summary>
    public sealed record Completed(AgentRunResult Result) : AgentStreamEvent;
}
