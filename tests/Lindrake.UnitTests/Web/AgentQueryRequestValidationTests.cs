// ============================================================
// Unit Tests — AgentQueryRequest / ConversationMessage bounds
// ============================================================

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Lindrake.Shared.DTOs;

namespace Lindrake.UnitTests.Web;

public sealed class AgentQueryRequestValidationTests
{
    [Fact]
    public void Request_WithinBounds_IsValid()
    {
        var request = new AgentQueryRequest
        {
            Query = "Who is Ada?",
            ConversationHistory = [Message("user", "hi"), Message("assistant", "hello")]
        };

        Validate(request).Should().BeEmpty();
    }

    [Fact]
    public void Request_TooManyHistoryMessages_IsInvalid()
    {
        var request = new AgentQueryRequest
        {
            Query = "Who is Ada?",
            ConversationHistory = [.. Enumerable.Repeat(Message("user", "hi"), AgentQueryRequest.MaxConversationTurns + 1)]
        };

        Validate(request).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(nameof(AgentQueryRequest.ConversationHistory));
    }

    [Theory]
    [InlineData("system")]
    [InlineData("tool")]
    [InlineData("")]
    public void Message_RoleOutsideUserAndAssistant_IsInvalid(string role)
    {
        Validate(Message(role, "hi")).Should().Contain(r => r.MemberNames.Contains(nameof(ConversationMessage.Role)));
    }

    [Fact]
    public void Message_ContentOverLimit_IsInvalid()
    {
        var message = Message("user", new string('x', ConversationMessage.MaxContentLength + 1));

        Validate(message).Should().ContainSingle()
            .Which.MemberNames.Should().Equal(nameof(ConversationMessage.Content));
    }

    private static ConversationMessage Message(string role, string content) => new() { Role = role, Content = content };

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
