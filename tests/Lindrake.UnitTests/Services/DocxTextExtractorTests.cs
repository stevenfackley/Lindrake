// ============================================================
// Unit Tests — DocxTextExtractor
// Builds a minimal .docx in-memory using DocumentFormat.OpenXml
// and verifies that the extractor returns the expected text.
// ============================================================

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class DocxTextExtractorTests
{
    private readonly DocxTextExtractor _sut = new(Mock.Of<ILogger<DocxTextExtractor>>());

    [Fact]
    public void SourceType_ReturnsDocx()
    {
        _sut.SourceType.Should().Be("docx");
    }

    [Fact]
    public async Task ExtractText_SingleParagraph_ReturnsText()
    {
        using var ms = BuildDocx("Hello, world!");

        var result = await _sut.ExtractTextAsync(ms, "test.docx");

        result.Should().Contain("Hello, world!");
    }

    [Fact]
    public async Task ExtractText_MultipleParagraphs_ReturnsAllText()
    {
        using var ms = BuildDocx("First paragraph.", "Second paragraph.", "Third paragraph.");

        var result = await _sut.ExtractTextAsync(ms, "test.docx");

        result.Should().Contain("First paragraph.");
        result.Should().Contain("Second paragraph.");
        result.Should().Contain("Third paragraph.");
    }

    [Fact]
    public async Task ExtractText_EmptyDocument_ReturnsEmpty()
    {
        // Document with no runs (empty paragraphs only)
        using var ms = BuildDocx();

        var result = await _sut.ExtractTextAsync(ms, "empty.docx");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractText_NullStream_ThrowsArgumentNull()
    {
        var act = async () => await _sut.ExtractTextAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractText_NonSeekableStream_StillExtractsText()
    {
        // Wrap the MemoryStream in a stream that reports CanSeek = false
        using var ms = BuildDocx("From a non-seekable stream");
        using var nonSeekable = new NonSeekableStream(ms);

        var result = await _sut.ExtractTextAsync(nonSeekable, "test.docx");

        result.Should().Contain("From a non-seekable stream");
    }

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Builds an in-memory .docx stream containing one paragraph per text entry.
    /// </summary>
    private static MemoryStream BuildDocx(params string[] paragraphTexts)
    {
        var ms = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            foreach (var text in paragraphTexts)
            {
                body.AppendChild(
                    new Paragraph(
                        new Run(
                            new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Stream wrapper that pretends to be non-seekable, to test the buffer path.
    /// </summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;   // override to return false
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
