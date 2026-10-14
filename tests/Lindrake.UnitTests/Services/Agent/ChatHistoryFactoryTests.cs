// ============================================================
// Unit Tests — ChatHistoryFactory
// ============================================================

using FluentAssertions;
using Lindrake.Api.Options;
using Lindrake.Api.Services.Agent;
using Lindrake.Shared.DTOs;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lindrake.UnitTests.Services.Agent;

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
            (AuthorRole.System, ResearchProfiles.FamilyHistory.SystemPrompt),
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

        history.Select(m => m.Content).Should().Equal(ResearchProfiles.FamilyHistory.SystemPrompt, "fresh start");
    }

    [Fact]
    public void Create_UsesTheConfiguredProfilePrompt()
    {
        var factory = Factory(maxHistoryMessages: 20, new ResearchProfileOptions { SystemPrompt = "You review insurance claims." });

        var history = factory.Create(new AgentQueryRequest { Query = "Is claim 7 covered?" });

        history[0].Role.Should().Be(AuthorRole.System);
        history[0].Content.Should().Be("You review insurance claims.");
    }

    private static ChatHistoryFactory Factory(int maxHistoryMessages, ResearchProfileOptions? profile = null) => new(
        Microsoft.Extensions.Options.Options.Create(new AgentOptions { MaxHistoryMessages = maxHistoryMessages }),
        Microsoft.Extensions.Options.Options.Create(profile ?? new ResearchProfileOptions()));
}
