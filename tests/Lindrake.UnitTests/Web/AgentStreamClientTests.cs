using System.Net;
using FluentAssertions;
using OmniSift.Shared.DTOs;
using OmniSift.UnitTests.TestSupport;
using OmniSift.Web.Services;
using static OmniSift.UnitTests.TestSupport.FakeHttpMessageHandler;

namespace OmniSift.UnitTests.Web;

/// <summary>OmniSiftApiClient.QueryAgentStreamAsync over a fake transport.</summary>
public sealed class AgentStreamClientTests
{
    private const string DeltaFrame = "data: {\"type\":\"delta\",\"content\":\"Hi\"}\n\n";
    private const string FinalFrame = "data: {\"type\":\"final\",\"pluginsUsed\":[],\"durationMs\":1,\"sources\":[]}\n\n";

    private static readonly AgentQueryRequest Request = new() { Query = "Who is John Doe?" };

    private static async Task<List<SseEvent>> ReadAllAsync(IAsyncEnumerable<SseEvent> events)
    {
        var list = new List<SseEvent>();
        await foreach (var evt in events)
            list.Add(evt);
        return list;
    }

    [Fact]
    public async Task Events_ArriveBeforeTheResponseEnds()
    {
        // The body never ends: only a reader that does not buffer it sees the first event.
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => ScriptedReadStream.Response(DeltaFrame)));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var events = harness.Api.QueryAgentStreamAsync(Request, cts.Token).GetAsyncEnumerator(cts.Token);

        (await events.MoveNextAsync()).Should().BeTrue();
        events.Current.Should().Be(new SseEvent.Delta("Hi"));
        var request = harness.Handler.Requests.Single();
        request.PathAndQuery.Should().Be("/api/agent/query/stream");
        request.HeaderNames.Should().Contain("Accept");
    }

    [Fact]
    public async Task Cancellation_AbortsTheStream()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => ScriptedReadStream.Response(DeltaFrame)));
        using var cts = new CancellationTokenSource();

        var act = async () =>
        {
            await foreach (var _ in harness.Api.QueryAgentStreamAsync(Request, cts.Token))
                await cts.CancelAsync(); // after the first event the body hangs until cancelled
        };

        await act.Should().ThrowAsync<OperationCanceledException>().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ErrorStatus_YieldsTheProblemDetailsMessage()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            Problem(HttpStatusCode.BadRequest, "Query must be at least 3 characters.")));

        var events = await ReadAllAsync(harness.Api.QueryAgentStreamAsync(Request));

        events.Should().Equal(new SseEvent.Error("Query must be at least 3 characters."));
    }

    [Fact]
    public async Task Unauthorized_SignsOut()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => Problem(HttpStatusCode.Unauthorized)));
        await harness.SignInAsync();

        var events = await ReadAllAsync(harness.Api.QueryAgentStreamAsync(Request));

        events.Should().ContainSingle().Which.Should().BeOfType<SseEvent.Error>();
        harness.Auth.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task NetworkFailure_YieldsAnError()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => throw new HttpRequestException("refused")));

        var events = await ReadAllAsync(harness.Api.QueryAgentStreamAsync(Request));

        events.Should().Equal(new SseEvent.Error(ApiError.Unreachable().Message));
    }

    [Fact]
    public async Task ConnectionDroppedMidStream_YieldsAnErrorAfterThePartialAnswer()
    {
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ =>
            ScriptedReadStream.Response(DeltaFrame, failWith: new IOException("connection reset"))));

        var events = await ReadAllAsync(harness.Api.QueryAgentStreamAsync(Request));

        events.Should().Equal(new SseEvent.Delta("Hi"), new SseEvent.Error(OmniSiftApiClient.StreamInterruptedMessage));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Response_IsDisposed_WhenTheEnumerationEnds(bool stopEarly)
    {
        var content = new TrackingContent(DeltaFrame + FinalFrame);
        using var harness = new WebClientHarness(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        await foreach (var _ in harness.Api.QueryAgentStreamAsync(Request))
        {
            if (stopEarly)
                break;
        }

        content.IsDisposed.Should().BeTrue();
    }
}
