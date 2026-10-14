// ============================================================
// Unit Tests — PlainTextExtractor
// Verifies decoding (UTF-8 default, BOM detection), line-ending
// normalization and the empty-file contract
// ============================================================

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _sut = new(Mock.Of<ILogger<PlainTextExtractor>>());

    [Fact]
    public void SourceType_ReturnsText()
    {
        _sut.SourceType.Should().Be("text");
    }

    [Fact]
    public async Task ExtractText_Utf8WithoutBom_DecodesNonAscii()
    {
        var result = await ExtractAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), "Seán Ó Briain — Corcaigh");

        result.Should().Be("Seán Ó Briain — Corcaigh");
    }

    [Fact]
    public async Task ExtractText_Utf8Bom_IsStripped()
    {
        var result = await ExtractAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), "Parish register");

        result.Should().Be("Parish register");
    }

    [Fact]
    public async Task ExtractText_Utf16LeBom_IsDetected()
    {
        var result = await ExtractAsync(new UnicodeEncoding(bigEndian: false, byteOrderMark: true), "Notepad export: café");

        result.Should().Be("Notepad export: café");
    }

    [Fact]
    public async Task ExtractText_Utf16BeBom_IsDetected()
    {
        var result = await ExtractAsync(new UnicodeEncoding(bigEndian: true, byteOrderMark: true), "Big-endian ü");

        result.Should().Be("Big-endian ü");
    }

    [Fact]
    public async Task ExtractText_MixedLineEndings_NormalizesToLf()
    {
        var result = await ExtractAsync(Encoding.UTF8, "line one\r\nline two\rline three\nline four");

        result.Should().Be("line one\nline two\nline three\nline four");
    }

    [Fact]
    public async Task ExtractText_Markdown_IsKeptVerbatim()
    {
        const string markdown = "# Springfield Gazette\n\n## Local businessman resurfaces\n\nSeen as **J. Donahue**.";

        var result = await ExtractAsync(Encoding.UTF8, markdown, "clipping.md");

        result.Should().Be(markdown);
    }

    [Fact]
    public async Task ExtractText_SurroundingWhitespace_IsTrimmed()
    {
        var result = await ExtractAsync(Encoding.UTF8, "\r\n\r\n  body text \t\r\n\r\n");

        result.Should().Be("body text");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t\r\n")]
    public async Task ExtractText_EmptyOrWhitespace_ReturnsEmpty(string content)
    {
        var result = await ExtractAsync(Encoding.UTF8, content);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractText_BomOnly_ReturnsEmpty()
    {
        using var stream = new MemoryStream([0xEF, 0xBB, 0xBF]);

        var result = await _sut.ExtractTextAsync(stream, "empty.txt");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractText_LeavesTheStreamOpen()
    {
        using var stream = new MemoryStream("text"u8.ToArray());

        await _sut.ExtractTextAsync(stream, "notes.txt");

        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractText_NullStream_Throws()
    {
        var act = () => _sut.ExtractTextAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<string> ExtractAsync(Encoding encoding, string content, string fileName = "notes.txt")
    {
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();
        using var stream = new MemoryStream(bytes);
        return await _sut.ExtractTextAsync(stream, fileName);
    }
}
