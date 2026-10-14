// ============================================================
// Unit Tests — AgentSseWriter
// The frames the API writes must be exactly what the Blazor
// client's SseParser understands.
// ============================================================

using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSift.Api.Services.Agent;
using OmniSift.Shared.DTOs;
using OmniSift.Web.Services;

namespace OmniSift.UnitTests.Services.Agent;

public sealed class AgentSseWriterTests
{
    private static readonly AgentRunResult Result = new(
        "answer",
        ["VectorSearch"],
        42,
        [new SourceCitation { Type = "web", Url = "https://example.test", Title = "Example", RelevanceScore = 0.9 }]);

    [Fact]
    public void DeltaFrame_IsParsedBySseParser()
    {
        var frame = AgentSseWriter.Format(new AgentStreamEvent.Delta("Hel\"lo\n"));

        frame.Should().StartWith("data: ").And.EndWith("\n\n");
        SseParser.ParseAll(frame).Should().Equal(new SseEvent.Delta("Hel\"lo\n"));
    }

    [Fact]
    public void FinalFrame_IsParsedBySseParser()
    {
        var frame = AgentSseWriter.Format(new AgentStreamEvent.Completed(Result));

        frame.Should().Be(
            """data: {"type":"final","pluginsUsed":["VectorSearch"],"durationMs":42,"sources":[{"type":"web","url":"https://example.test","title":"Example","relevanceScore":0.9}]}""" + "\n\n");
        var final = SseParser.ParseAll(frame).Should().ContainSingle().Which.Should().BeOfType<SseEvent.Final>().Subject;
        final.Data.PluginsUsed.Should().Equal("VectorSearch");
        final.Data.DurationMs.Should().Be(42);
        final.Data.Sources.Should().ContainSingle().Which.Url.Should().Be("https://example.test");
    }

    [Fact]
    public void ErrorFrame_IsParsedBySseParser()
    {
        var frame = AgentSseWriter.ErrorFrame(AgentSseWriter.StreamFailedMessage);

        frame.Should().Be($$"""data: {"type":"error","message":"{{AgentSseWriter.StreamFailedMessage}}"}""" + "\n\n");
        SseParser.ParseAll(frame).Should().Equal(new SseEvent.Error(AgentSseWriter.StreamFailedMessage));
    }

    [Fact]
    public async Task WrittenStream_RoundTripsThroughTheClientReader()
    {
        var context = NewContext(out var body);
        await new AgentSseWriter(NullLogger<AgentSseWriter>.Instance).WriteAsync(
            context.Response,
            Events(new AgentStreamEvent.Delta("Hel"), new AgentStreamEvent.Delta("lo"), new AgentStreamEvent.Completed(Result)),
            CancellationToken.None);
        var text = Encoding.UTF8.GetString(body.ToArray()) + AgentSseWriter.ErrorFrame("boom");

        var events = new List<SseEvent>();
        await foreach (var evt in SseParser.ReadEventsAsync(new StringReader(text)))
            events.Add(evt);

        events.Should().HaveCount(4);
        events[0].Should().Be(new SseEvent.Delta("Hel"));
        events[1].Should().Be(new SseEvent.Delta("lo"));
        events[2].Should().BeOfType<SseEvent.Final>().Which.Data.DurationMs.Should().Be(42);
        events[3].Should().Be(new SseEvent.Error("boom"));
    }

    [Fact]
    public async Task WriteAsync_WritesEventStreamHeadersAndFrames()
    {
        var context = NewContext(out var body);

        await new AgentSseWriter(NullLogger<AgentSseWriter>.Instance).WriteAsync(
            context.Response,
            Events(new AgentStreamEvent.Delta("Hi"), new AgentStreamEvent.Completed(Result)),
            CancellationToken.None);

        context.Response.ContentType.Should().Be("text/event-stream");
        context.Response.Headers.CacheControl.ToString().Should().Be("no-cache");
        Encoding.UTF8.GetString(body.ToArray()).Should().Be(
            AgentSseWriter.Format(new AgentStreamEvent.Delta("Hi")) +
            AgentSseWriter.Format(new AgentStreamEvent.Completed(Result)));
    }

    [Fact]
    public async Task WriteAsync_FailureBeforeResponseStarted_Propagates()
    {
        // Nothing is committed yet, so the exception handler can still send ProblemDetails.
        var context = NewContext(out _);

        var act = () => new AgentSseWriter(NullLogger<AgentSseWriter>.Instance).WriteAsync(
            context.Response, Failing(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static DefaultHttpContext NewContext(out MemoryStream body)
    {
        body = new MemoryStream();
        return new DefaultHttpContext { Response = { Body = body } };
    }

    private static async IAsyncEnumerable<AgentStreamEvent> Events(params AgentStreamEvent[] events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<AgentStreamEvent> Failing()
    {
        await Task.Yield();
        throw new InvalidOperationException("secret internals");
#pragma warning disable CS0162 // Unreachable code: makes this an iterator.
        yield break;
#pragma warning restore CS0162
    }
}
