// ============================================================
// OmniSift.Api — Chat history construction for the research agent
// ============================================================

using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using OmniSift.Api.Options;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Services.Agent;

/// <summary>
/// Builds the LLM <see cref="ChatHistory"/>: the research profile's system prompt,
/// the most recent <see cref="AgentOptions.MaxHistoryMessages"/> conversation
/// messages, then the query.
/// </summary>
public sealed class ChatHistoryFactory(
    IOptions<AgentOptions> agentOptions,
    IOptions<ResearchProfileOptions> profileOptions)
{
    public ChatHistory Create(AgentQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var history = new ChatHistory();
        history.AddSystemMessage(profileOptions.Value.SystemPrompt);

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
