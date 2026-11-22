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
    [InlineData(3)]
    [InlineData(5000)]
    public void Request_QueryLengthAtABound_IsValid(int length)
    {
        Validate(new AgentQueryRequest { Query = new string('q', length) }).Should().BeEmpty();
    }

    [Theory]
    [InlineData(2, "Query must be at least 3 characters.")]
    [InlineData(5001, "Query must not exceed 5000 characters.")]
    public void Request_QueryLengthJustOutsideABound_IsInvalidWithTheBoundInTheMessage(int length, string message)
    {
        var results = Validate(new AgentQueryRequest { Query = new string('q', length) });

        var result = results.Should().ContainSingle().Subject;
        result.MemberNames.Should().Equal(nameof(AgentQueryRequest.Query));
        result.ErrorMessage.Should().Be(message);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("assistant")]
    public void Message_RoleUserOrAssistant_IsValid(string role)
    {
        Validate(Message(role, "hi")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("system")]
    [InlineData("tool")]
    [InlineData("")]
    public void Message_RoleOutsideUserAndAssistant_IsInvalid(string role)
    {
        Validate(Message(role, "hi")).Should().Contain(r => r.MemberNames.Contains(nameof(ConversationMessage.Role)));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("ASSISTANT")]
    public void Message_RoleInAnotherCase_IsInvalid(string role)
    {
        // The API validates the role case-sensitively (the agent and the reports then read it in any case).
        var result = Validate(Message(role, "hi")).Should().ContainSingle().Subject;
        result.MemberNames.Should().Equal(nameof(ConversationMessage.Role));
        result.ErrorMessage.Should().Be("Role must be 'user' or 'assistant'.");
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
