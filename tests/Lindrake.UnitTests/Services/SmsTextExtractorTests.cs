// ============================================================
// Unit Tests — SmsTextExtractor
// Verifies CSV and JSON SMS parsing
// ============================================================

using System.Text;
using FluentAssertions;
using Lindrake.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lindrake.UnitTests.Services;

public sealed class SmsTextExtractorTests
{
    private readonly SmsTextExtractor _sut;

    public SmsTextExtractorTests()
    {
        _sut = new SmsTextExtractor(Mock.Of<ILogger<SmsTextExtractor>>());
    }

    [Fact]
    public void SourceType_ReturnsSms()
    {
        _sut.SourceType.Should().Be("sms");
    }

    [Fact]
    public async Task ExtractText_CsvWithHeader_ParsesMessages()
    {
        var csv = """
            sender,timestamp,message
            Alice,2024-01-01 10:00,Hello Bob
            Bob,2024-01-01 10:05,Hi Alice how are you?
            Alice,2024-01-01 10:10,I am good thanks
            """;

        var result = await ExtractFromString(csv);

        result.Should().Contain("[SMS Conversation]");
        result.Should().Contain("Alice");
        result.Should().Contain("Bob");
        result.Should().Contain("Hello Bob");
        result.Should().Contain("Hi Alice how are you?");
    }

    [Fact]
    public async Task ExtractText_CsvWithQuotedFields_HandlesCorrectly()
    {
        var csv = """
            sender,timestamp,message
            Alice,2024-01-01,"Hello, Bob"
            Bob,2024-01-01,"She said ""hi"" to me"
            """;

        var result = await ExtractFromString(csv);

        result.Should().Contain("Hello, Bob");
        result.Should().Contain("She said \"hi\" to me");
    }

    [Fact]
    public async Task ExtractText_JsonArray_ParsesMessages()
    {
        var json = """
            [
                {"sender": "Alice", "timestamp": "2024-01-01 10:00", "message": "Hello"},
                {"sender": "Bob", "timestamp": "2024-01-01 10:05", "message": "World"}
            ]
            """;

        var result = await ExtractFromString(json);

        result.Should().Contain("[SMS Conversation]");
        result.Should().Contain("Alice");
        result.Should().Contain("Hello");
        result.Should().Contain("Bob");
        result.Should().Contain("World");
    }

    [Fact]
    public async Task ExtractText_JsonSingleObject_ParsesMessage()
    {
        var json = """{"sender": "Alice", "message": "Single message"}""";

        var result = await ExtractFromString(json);

        result.Should().Contain("Alice");
        result.Should().Contain("Single message");
    }

    [Fact]
    public async Task ExtractText_JsonWithAlternateFieldNames_StillParses()
    {
        var json = """
            [
                {"from": "Alice", "date": "2024-01-01", "body": "Alternative fields"}
            ]
            """;

        var result = await ExtractFromString(json);

        result.Should().Contain("Alice");
        result.Should().Contain("Alternative fields");
    }

    [Fact]
    public async Task ExtractText_CsvQuotedNewline_StaysInOneMessage()
    {
        var csv = "sender,timestamp,message\r\n"
            + "Alice,2024-01-01,\"first line\nsecond line\"\r\n"
            + "Bob,2024-01-02,reply\r\n";

        var result = await ExtractFromString(csv);

        result.Should().Be(
            "[SMS Conversation]\n"
            + "Alice (2024-01-01) first line\nsecond line\n"
            + "Bob (2024-01-02) reply");
    }

    [Fact]
    public async Task ExtractText_CsvAndJson_FormatMessagesIdentically()
    {
        var csv = await ExtractFromString("sender,timestamp,message\nAlice,2024-01-01,Hi\n");
        var json = await ExtractFromString("""[{ "sender": "Alice", "timestamp": "2024-01-01", "message": "Hi" }]""");

        csv.Should().Be(json).And.Be("[SMS Conversation]\nAlice (2024-01-01) Hi");
    }

    [Fact]
    public async Task ExtractText_EmptyStream_ReturnsEmpty()
    {
        var result = await ExtractFromString(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractText_CsvHeaderOnly_ReturnsEmpty()
    {
        var csv = "sender,timestamp,message\n";
        var result = await ExtractFromString(csv);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractText_InvalidJson_ThrowsInvalidOperation()
    {
        var badJson = "[{invalid json}]";

        var act = async () => await ExtractFromString(badJson);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExtractText_NullStream_ThrowsArgumentNull()
    {
        var act = async () => await _sut.ExtractTextAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<string> ExtractFromString(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await _sut.ExtractTextAsync(stream, "test.csv");
    }
}
