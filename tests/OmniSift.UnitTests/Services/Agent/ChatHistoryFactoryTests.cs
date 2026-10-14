// ============================================================
// Unit Tests — ChatHistoryFactory
// ============================================================

using FluentAssertions;
using Microsoft.SemanticKernel.ChatCompletion;
using OmniSift.Api.Options;
using OmniSift.Api.Services.Agent;
using OmniSift.Shared.DTOs;

namespace OmniSift.UnitTests.Services.Agent;

public sealed class ChatHistoryFactoryTests
{
    [Fact]
    public void Create_OrdersSystemPromptHistoryThenQuery()
    {
        var history = Factory(maxHistoryMessages: 20).Create(new AgentQueryRequest
        {
            Query = "and then?",
            ConversationHistory =
            [
                new ConversationMessage { Role = "user", Content = "q1" },
                new ConversationMessage { Role = "Assistant", Content = "a1" }
            ]
        });

        history.Select(m => (m.Role, m.Content)).Should().Equal(
            (AuthorRole.System, ChatHistoryFactory.SystemPrompt),
            (AuthorRole.User, "q1"),
            (AuthorRole.Assistant, "a1"),
            (AuthorRole.User, "and then?"));
    }

    [Fact]
    public void Create_CapIsZero_SendsNoHistory()
    {
        var history = Factory(maxHistoryMessages: 0).Create(new AgentQueryRequest
        {
            Query = "fresh start",
            ConversationHistory = [new ConversationMessage { Role = "user", Content = "old" }]
        });

        history.Select(m => m.Content).Should().Equal(ChatHistoryFactory.SystemPrompt, "fresh start");
    }

    private static ChatHistoryFactory Factory(int maxHistoryMessages) =>
        new(Microsoft.Extensions.Options.Options.Create(new AgentOptions { MaxHistoryMessages = maxHistoryMessages }));
}
