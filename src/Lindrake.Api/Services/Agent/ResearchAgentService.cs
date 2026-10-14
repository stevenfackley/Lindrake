// ============================================================
// Lindrake.Api — Research agent
// One code path for the buffered and the streamed query endpoints:
// chat history, execution settings, plugin + citation collection,
// query-history persistence.
// ============================================================

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Lindrake.Api.Middleware;
using Lindrake.Shared.DTOs;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lindrake.Api.Services.Agent;

/// <summary>Semantic Kernel implementation of <see cref="IResearchAgentService"/>.</summary>
public sealed class ResearchAgentService(
    Kernel kernel,
    ChatHistoryFactory chatHistoryFactory,
    IInvocationTracker invocationTracker,
    ICitationAccumulator citations,
    IQueryHistoryRecorder historyRecorder,
    ITenantContext tenantContext,
    ILogger<ResearchAgentService> logger) : IResearchAgentService
{
    /// <summary>Answer recorded when the model produced no text.</summary>
    internal const string NoResponseText = "I was unable to generate a response.";

    public async Task<AgentRunResult> RunAsync(AgentQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chatService = kernel.GetChatCompletionOrThrow();
        var stopwatch = Stopwatch.StartNew();
        LogStarted(request, "agent_query");

        var message = await chatService.GetChatMessageContentAsync(
            chatHistoryFactory.Create(request),
            CreateExecutionSettings(),
            kernel,
            cancellationToken).ConfigureAwait(false);

        return await CompleteAsync(request, message.Content, stopwatch, "agent_query").ConfigureAwait(false);
    }

    public IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Eager, so the caller can still answer a missing LLM with a plain 503
        // before it commits to an event stream.
        var chatService = kernel.GetChatCompletionOrThrow();
        return StreamCoreAsync(chatService, request, cancellationToken);
    }

    private async IAsyncEnumerable<AgentStreamEvent> StreamCoreAsync(
        IChatCompletionService chatService,
        AgentQueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogStarted(request, "agent_query_stream");

        var fullText = new StringBuilder();

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
            chatHistoryFactory.Create(request),
            CreateExecutionSettings(),
            kernel,
            cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(chunk.Content))
                continue;

            fullText.Append(chunk.Content);
            yield return new AgentStreamEvent.Delta(chunk.Content);
        }

        // Recorded before the completion event is handed out: once the consumer has
        // it, it may stop enumerating (or the client may be gone), and nothing after
        // the last yield would run.
        var result = await CompleteAsync(request, fullText.ToString(), stopwatch, "agent_query_stream")
            .ConfigureAwait(false);

        yield return new AgentStreamEvent.Completed(result);
    }

    private static PromptExecutionSettings CreateExecutionSettings() => new()
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    };

    private async Task<AgentRunResult> CompleteAsync(
        AgentQueryRequest request,
        string? responseText,
        Stopwatch stopwatch,
        string auditAction)
    {
        stopwatch.Stop();

        var result = new AgentRunResult(
            Response: string.IsNullOrEmpty(responseText) ? NoResponseText : responseText,
            PluginsUsed: invocationTracker.PluginsUsed,
            DurationMs: (int)stopwatch.ElapsedMilliseconds,
            Sources: citations.GetCitations());

        await historyRecorder.RecordAsync(request.Query, result, auditAction).ConfigureAwait(false);

        logger.LogInformation(
            "Agent {Action} completed in {DurationMs}ms, plugins={Plugins}, sources={SourceCount}",
            auditAction, result.DurationMs, string.Join(", ", result.PluginsUsed), result.Sources.Count);

        return result;
    }

    // Query text is user content (often PII) — log its size, never the text.
    private void LogStarted(AgentQueryRequest request, string action) =>
        logger.LogInformation(
            "Agent {Action} from tenant {TenantId}: query length {QueryLength}, history messages {HistoryCount}",
            action, tenantContext.TenantId, request.Query.Length, request.ConversationHistory?.Count ?? 0);
}
