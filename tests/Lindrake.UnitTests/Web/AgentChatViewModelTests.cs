using System.Net;
using System.Text.Json;
using FluentAssertions;
using OmniSift.UnitTests.TestSupport;
using OmniSift.Web.ViewModels;
using static OmniSift.UnitTests.TestSupport.FakeHttpMessageHandler;

namespace OmniSift.UnitTests.Web;

public sealed class AgentChatViewModelTests
{
    private const string Final = """{"type":"final","pluginsUsed":["VectorSearch"],"durationMs":42,"sources":[{"type":"document","title":"Doc"}]}""";

    private static string Delta(string text) => JsonSerializer.Serialize(new { type = "delta", content = text });

    private static string Sse(params string[] frames) => string.Concat(frames.Select(f => $"data: {f}\n\n"));

    private static HttpResponseMessage Stream(params string[] frames) => Json(HttpStatusCode.OK, Sse(frames), "text/event-stream");

    [Fact]
    public async Task Deltas_Accumulate_AndFinalCompletesTheAnswer()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Stream(Delta("Hel"), Delta("lo, "), Delta("world"), Final)));
        using var vm = new AgentChatViewModel(harness.Api);
        var seen = new List<string>();
        vm.Changed += () => seen.Add(vm.StreamingText);

        await vm.SendAsync("  Who is John Doe?  ");

        seen.Should().ContainInOrder("Hel", "Hello, ", "Hello, world");
        vm.IsStreaming.Should().BeFalse();
        vm.StreamingText.Should().BeEmpty();
        vm.Error.Should().BeNull();
        vm.Messages.Should().HaveCount(2);
        vm.Messages[0].Should().Be(new ChatMessage(ChatRoles.User, "Who is John Doe?"));
        var answer = vm.Messages[1];
        answer.Content.Should().Be("Hello, world");
        answer.IsIncomplete.Should().BeFalse();
        answer.PluginsUsed.Should().Equal("VectorSearch");
        answer.DurationMs.Should().Be(42);
        answer.Sources.Should().ContainSingle().Which.Title.Should().Be("Doc");
        harness.Handler.Requests.Single().PathAndQuery.Should().Be("/api/agent/query/stream");
    }

    [Fact]
    public async Task FollowUp_SendsTheWholeHistory_WithoutAClientSideTrim()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Stream(Delta("answer"), Final)));
        using var vm = new AgentChatViewModel(harness.Api);

        for (var i = 0; i < 7; i++)
            await vm.SendAsync($"question {i}");

        using var body = JsonDocument.Parse(harness.Handler.Requests.Last().Body!);
        var history = body.RootElement.GetProperty("conversationHistory").EnumerateArray().ToList();
        history.Should().HaveCount(12, "six earlier exchanges — more than the old TakeLast(10)");
        history[0].GetProperty("role").GetString().Should().Be("user");
        history[0].GetProperty("content").GetString().Should().Be("question 0");
        history[1].GetProperty("role").GetString().Should().Be("assistant");
    }

    [Fact]
    public async Task History_StaysWithinTheRequestValidationLimit()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Stream(Delta("answer"), Final)));
        using var vm = new AgentChatViewModel(harness.Api);

        for (var i = 0; i < 30; i++)
            await vm.SendAsync($"question {i}");

        using var body = JsonDocument.Parse(harness.Handler.Requests.Last().Body!);
        var history = body.RootElement.GetProperty("conversationHistory").EnumerateArray().ToList();
        history.Should().HaveCount(OmniSift.Shared.DTOs.AgentQueryRequest.MaxConversationTurns);
        history[0].GetProperty("role").GetString().Should().Be("user");
        history[^1].GetProperty("content").GetString().Should().Be("answer");
    }

    [Fact]
    public async Task ErrorFrame_IsShown_PartialTextIsKept_AndNothingIsRetried()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            Stream(Delta("Partial ans"), """{"type":"error","message":"The research agent could not finish this response."}""")));
        using var vm = new AgentChatViewModel(harness.Api);

        await vm.SendAsync("Who is John Doe?");

        vm.Error.Should().Be("The research agent could not finish this response.");
        vm.IsStreaming.Should().BeFalse();
        vm.Messages.Should().HaveCount(2);
        vm.Messages[1].Should().Be(new ChatMessage(ChatRoles.Assistant, "Partial ans") { IsIncomplete = true });
        harness.Handler.Requests.Should().ContainSingle("a failed stream is not re-run through the non-streaming endpoint");
    }

    [Fact]
    public async Task FailedExchange_IsNotSentBackAsHistory()
    {
        using var harness = new WebClientHarness(Sequence(
            () => Stream(Delta("Partial"), """{"type":"error","message":"boom"}"""),
            () => Stream(Delta("ok"), Final)));
        using var vm = new AgentChatViewModel(harness.Api);

        await vm.SendAsync("first question");
        await vm.SendAsync("second question");

        harness.Handler.Requests.Last().Body.Should().NotContain("conversationHistory");
        vm.Error.Should().BeNull("a new question clears the previous error");
    }

    [Fact]
    public async Task StreamEndingWithoutFinal_IsAnIncompleteResponse()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Stream(Delta("cut"))));
        using var vm = new AgentChatViewModel(harness.Api);

        await vm.SendAsync("Who is John Doe?");

        vm.Error.Should().Be(AgentChatViewModel.IncompleteResponseMessage);
        vm.Messages[^1].IsIncomplete.Should().BeTrue();
    }

    [Fact]
    public async Task Stop_CancelsTheStream_AndKeepsThePartialText()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => ScriptedReadStream.Response(Sse(Delta("Hel")))));
        using var vm = new AgentChatViewModel(harness.Api);
        var firstDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Changed += () =>
        {
            if (vm.StreamingText == "Hel")
                firstDelta.TrySetResult();
        };

        var send = vm.SendAsync("Who is John Doe?");
        await firstDelta.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.IsStreaming.Should().BeTrue();
        vm.Stop();
        await send.WaitAsync(TimeSpan.FromSeconds(10));

        vm.IsStreaming.Should().BeFalse();
        vm.Error.Should().BeNull();
        vm.Messages[^1].Should().Be(new ChatMessage(ChatRoles.Assistant, "Hel") { IsIncomplete = true });
    }

    [Fact]
    public async Task Dispose_CancelsTheStream()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => ScriptedReadStream.Response(Sse(Delta("Hel")))));
        var vm = new AgentChatViewModel(harness.Api);
        var firstDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Changed += () =>
        {
            if (vm.StreamingText == "Hel")
                firstDelta.TrySetResult();
        };

        var send = vm.SendAsync("Who is John Doe?");
        await firstDelta.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.Dispose();

        await send.WaitAsync(TimeSpan.FromSeconds(10)); // completes, does not throw
        vm.Messages.Should().ContainSingle("nothing is added once the page is gone");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  hi  ", false)]
    [InlineData("why", true)]
    public void CanSend_RequiresTheApiMinimumLength(string? query, bool expected)
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Stream(Final)));
        using var vm = new AgentChatViewModel(harness.Api);

        vm.CanSend(query).Should().Be(expected);
    }

    [Fact]
    public async Task Report_ReturnsThePdf()
    {
        using var harness = new WebClientHarness(Sequence(
            () => Stream(Delta("answer"), Final),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) }));
        using var vm = new AgentChatViewModel(harness.Api);
        await vm.SendAsync("Who is John Doe?");

        var pdf = await vm.GenerateReportAsync();

        pdf.Should().Equal(0x25, 0x50, 0x44, 0x46);
        vm.IsGeneratingReport.Should().BeFalse();
        var request = harness.Handler.Requests.Last();
        request.PathAndQuery.Should().Be("/api/agent/report/pdf");
        request.Body.Should().Contain("\"title\":\"OmniSift Research Report\"").And.Contain("\"role\":\"assistant\"");
    }

    [Fact]
    public async Task Report_Failure_ShowsTheProblemDetails()
    {
        using var harness = new WebClientHarness(Sequence(
            () => Stream(Delta("answer"), Final),
            () => Problem(HttpStatusCode.BadRequest, "Report has too many messages.")));
        using var vm = new AgentChatViewModel(harness.Api);
        await vm.SendAsync("Who is John Doe?");

        (await vm.GenerateReportAsync()).Should().BeNull();

        vm.Error.Should().Be("PDF export failed: Report has too many messages.");
        vm.IsGeneratingReport.Should().BeFalse();
    }

    [Fact]
    public async Task RequestRejected_ShowsTheProblemDetails()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            Problem(HttpStatusCode.ServiceUnavailable, "The language model is not configured.")));
        using var vm = new AgentChatViewModel(harness.Api);

        await vm.SendAsync("Who is John Doe?");

        vm.Error.Should().Be("The language model is not configured.");
        vm.Messages.Should().ContainSingle().Which.Role.Should().Be(ChatRoles.User);
        vm.IsStreaming.Should().BeFalse();
    }
}
