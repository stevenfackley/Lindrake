using FluentAssertions;
using Lindrake.Web.Services;

namespace Lindrake.UnitTests.Web;

public sealed class SseParserTests
{
    private const string DeltaHello = """{"type":"delta","content":"Hello"}""";

    [Fact]
    public void Delta_ReturnsDeltaEvent()
    {
        SseParser.ParseAll($"data: {DeltaHello}\n\n").Should().Equal(new SseEvent.Delta("Hello"));
    }

    [Fact]
    public void Final_ReturnsFinalEvent()
    {
        var events = SseParser.ParseAll("""data: {"type":"final","pluginsUsed":["VectorSearch"],"durationMs":42,"sources":[{"type":"document","title":"Test Doc"}]}""" + "\n\n");

        var final = events.Should().ContainSingle().Which.Should().BeOfType<SseEvent.Final>().Subject;
        final.Data.DurationMs.Should().Be(42);
        final.Data.PluginsUsed.Should().Equal("VectorSearch");
        final.Data.Sources.Should().ContainSingle().Which.Title.Should().Be("Test Doc");
    }

    [Fact]
    public void Error_ReturnsErrorEventWithTheMessage()
    {
        SseParser.ParseAll("""data: {"type":"error","message":"Agent failed."}""" + "\n\n")
            .Should().Equal(new SseEvent.Error("Agent failed."));
    }

    [Theory]
    [InlineData("""{"type":"error"}""")]
    [InlineData("""{"type":"error","message":""}""")]
    [InlineData("""{"type":"error","message":7}""")]
    public void Error_WithoutAMessage_UsesTheDefault(string json)
    {
        SseParser.ParseAll($"data: {json}\n\n").Should().Equal(new SseEvent.Error(SseEvent.Error.DefaultMessage));
    }

    [Theory]
    [InlineData("data: ")]
    [InlineData("data:")]
    public void DataWithOrWithoutTheSpace_IsAccepted(string prefix)
    {
        SseParser.ParseAll($"{prefix}{DeltaHello}\n\n").Should().Equal(new SseEvent.Delta("Hello"));
    }

    [Fact]
    public void OnlyOneLeadingSpace_IsStripped()
    {
        // The JSON tolerates the remaining space; the content is untouched.
        SseParser.ParseAll("""data:   {"type":"delta","content":"  x"}""" + "\n\n").Should().Equal(new SseEvent.Delta("  x"));
    }

    [Fact]
    public void MultipleDataLines_AreJoinedWithNewlines()
    {
        var parser = new SseParser();

        parser.PushLine("""data: {"type":"delta",""").Should().BeNull();
        parser.PushLine("""data: "content":"split"}""").Should().BeNull();
        parser.PushLine(string.Empty).Should().Be(new SseEvent.Delta("split"));
    }

    [Fact]
    public void MultipleDataLines_AreJoinedWithALineFeed_NotConcatenated()
    {
        // Joined with "\n" the string literal holds a raw newline (invalid JSON, so
        // ignored); concatenated it would wrongly parse as "abcd".
        var parser = new SseParser();
        parser.PushLine("""data: {"type":"delta","content":"ab""");
        parser.PushLine("""data: cd"}""");

        parser.PushLine(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Event_IsDispatchedOnlyOnABlankLine()
    {
        var parser = new SseParser();

        parser.PushLine($"data: {DeltaHello}").Should().BeNull();
        parser.PushLine(string.Empty).Should().Be(new SseEvent.Delta("Hello"));
        parser.PushLine(string.Empty).Should().BeNull("the buffer was reset by the dispatch");
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void AllLineEndings_AreSupported(string eol)
    {
        var text = $"data: {DeltaHello}{eol}{eol}: keep-alive{eol}data: {"""{"type":"delta","content":"!"}"""}{eol}{eol}";

        SseParser.ParseAll(text).Should().Equal(new SseEvent.Delta("Hello"), new SseEvent.Delta("!"));
    }

    [Theory]
    [InlineData(": keep-alive")]
    [InlineData(":")]
    [InlineData("event: message")]
    [InlineData("event:delta")]
    [InlineData("id: 42")]
    [InlineData("retry: 3000")]
    [InlineData("unknown: field")]
    public void CommentsAndOtherFields_AreIgnored(string line)
    {
        SseParser.ParseAll($"{line}\ndata: {DeltaHello}\n{line}\n\n").Should().Equal(new SseEvent.Delta("Hello"));
    }

    [Fact]
    public void FieldWithoutColon_HasAnEmptyValue()
    {
        // "data" alone appends an empty line: the payload becomes "\n{...}", still valid JSON.
        SseParser.ParseAll($"data\ndata: {DeltaHello}\n\n").Should().Equal(new SseEvent.Delta("Hello"));
    }

    [Theory]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"content":"no type"}""")]
    [InlineData("""{"type":5}""")]
    [InlineData("{not valid json}")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void UnknownOrMalformedPayloads_AreIgnored(string json)
    {
        SseParser.ParseAll($"data: {json}\n\ndata: {DeltaHello}\n\n").Should().Equal(new SseEvent.Delta("Hello"));
    }

    [Fact]
    public void BlankLinesWithoutData_DispatchNothing()
    {
        SseParser.ParseAll("\n\n\n").Should().BeEmpty();
    }

    [Fact]
    public void UnterminatedFinalEvent_IsDiscarded()
    {
        SseParser.ParseAll($"data: {DeltaHello}\n\ndata: {DeltaHello}\n").Should().ContainSingle();
    }

    [Theory]
    [InlineData("""{"type":"delta","content":""}""")]
    [InlineData("""{"type":"delta"}""")]
    public void DeltaWithoutContent_IsEmpty(string json)
    {
        SseParser.ParseAll($"data: {json}\n\n").Should().Equal(new SseEvent.Delta(string.Empty));
    }

    [Fact]
    public void EscapedNewlinesInContent_ArePreserved()
    {
        SseParser.ParseAll("""data: {"type":"delta","content":"line1\nline2"}""" + "\n\n")
            .Should().Equal(new SseEvent.Delta("line1\nline2"));
    }

    [Fact]
    public async Task ReadEventsAsync_StreamsEventsInOrder()
    {
        var text = $"data: {DeltaHello}\r\n\r\ndata: {"""{"type":"error","message":"x"}"""}\r\n\r\n";

        var events = new List<SseEvent>();
        await foreach (var evt in SseParser.ReadEventsAsync(new StringReader(text)))
            events.Add(evt);

        events.Should().Equal(new SseEvent.Delta("Hello"), new SseEvent.Error("x"));
    }

    [Fact]
    public async Task ReadEventsAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
        {
            await foreach (var _ in SseParser.ReadEventsAsync(new StringReader($"data: {DeltaHello}\n\n"), cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
