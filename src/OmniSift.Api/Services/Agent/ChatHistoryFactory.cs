// ============================================================
// OmniSift.Api — Chat history construction for the research agent
// ============================================================

using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using OmniSift.Api.Options;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Services.Agent;

/// <summary>
/// Builds the LLM <see cref="ChatHistory"/>: system prompt, the most recent
/// <see cref="AgentOptions.MaxHistoryMessages"/> conversation messages, then the query.
/// </summary>
public sealed class ChatHistoryFactory(IOptions<AgentOptions> agentOptions)
{
    /// <summary>System prompt for the research agent.</summary>
    internal const string SystemPrompt = """
        You are OmniSift, an AI research assistant. You help users find and analyze information
        from their uploaded documents and the web.

        Guidelines:
        - When answering questions, ALWAYS search the user's documents first using SearchDocuments.
        - If the documents don't contain sufficient information, supplement with web search using SearchWeb.
        - For questions about historical web pages, use GetArchivedPage to check the Wayback Machine.
        - Always cite your sources clearly. For document results, mention the file name and relevance.
        - For web results, include the URL.
        - Be thorough but concise. Present findings in a structured manner.
        - If you cannot find relevant information, say so honestly rather than speculating.
        - When presenting multiple pieces of information, organize them logically.
        """;

    public ChatHistory Create(AgentQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);

        var messages = (request.ConversationHistory ?? [])
            .Where(m => IsRole(m, "user") || IsRole(m, "assistant"))
            .TakeLast(agentOptions.Value.MaxHistoryMessages);

        foreach (var message in messages)
        {
            if (IsRole(message, "user"))
                history.AddUserMessage(message.Content);
            else
                history.AddAssistantMessage(message.Content);
        }

        history.AddUserMessage(request.Query);
        return history;
    }

    private static bool IsRole(ConversationMessage message, string role) =>
        string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase);
}
