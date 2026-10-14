// ============================================================
// Unit Tests — TextExtractorRegistry
// ============================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSift.Api.Services;
using OmniSift.Api.Services.Extraction;

namespace OmniSift.UnitTests.Services;

public sealed class TextExtractorRegistryTests
{
    private static readonly TextExtractorRegistry Registry = new(
    [
        new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance),
        new SmsTextExtractor(NullLogger<SmsTextExtractor>.Instance),
        new WebTextExtractor(NullLogger<WebTextExtractor>.Instance),
        new DocxTextExtractor(NullLogger<DocxTextExtractor>.Instance),
        new EmailTextExtractor(NullLogger<EmailTextExtractor>.Instance),
        new PlainTextExtractor(NullLogger<PlainTextExtractor>.Instance),
    ]);

    [Fact]
    public void SupportedSourceTypes_ListsEveryExtractorSorted()
    {
        Registry.SupportedSourceTypes.Should().Equal("docx", "email", "pdf", "sms", "text", "web");
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("PDF")]
    [InlineData("Email")]
    public void TryGet_KnownSourceType_IgnoresCase(string sourceType)
    {
        Registry.TryGet(sourceType, out var extractor).Should().BeTrue();
        extractor!.SourceType.Should().Be(sourceType.ToLowerInvariant());
    }

    [Theory]
    [InlineData("spreadsheet")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGet_UnknownSourceType_ReturnsFalse(string? sourceType)
    {
        Registry.TryGet(sourceType, out var extractor).Should().BeFalse();
        extractor.Should().BeNull();
    }

    [Fact]
    public void Constructor_DuplicateSourceType_Throws()
    {
        var a = Mock.Of<ITextExtractor>(e => e.SourceType == "pdf");
        var b = Mock.Of<ITextExtractor>(e => e.SourceType == "PDF");

        var act = () => new TextExtractorRegistry([a, b]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("application/pdf", "scan.bin", "pdf")]
    [InlineData("text/csv; charset=utf-8", null, "sms")]
    [InlineData("application/json", "export.json", "sms")]
    [InlineData("text/html", "page.html", "web")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "a.docx", "docx")]
    [InlineData("message/rfc822", "mail.eml", "email")]
    [InlineData("APPLICATION/MBOX", "inbox.mbox", "email")]
    [InlineData("text/plain; charset=utf-8", "notes.txt", "text")]
    [InlineData("text/markdown", "README.md", "text")]
    public void TryResolveSourceType_ByMimeType(string contentType, string? fileName, string expected)
    {
        Registry.TryResolveSourceType(contentType, fileName, out var sourceType).Should().BeTrue();
        sourceType.Should().Be(expected);
    }

    // The Blazor client sends application/octet-stream for every upload.
    [Theory]
    [InlineData("report.pdf", "pdf")]
    [InlineData("REPORT.PDF", "pdf")]
    [InlineData("letter.docx", "docx")]
    [InlineData("message.eml", "email")]
    [InlineData("archive.mbox", "email")]
    [InlineData("texts.csv", "sms")]
    [InlineData("texts.json", "sms")]
    [InlineData("page.html", "web")]
    [InlineData("page.htm", "web")]
    [InlineData("notes.txt", "text")]
    [InlineData("README.MD", "text")]
    public void TryResolveSourceType_OctetStream_FallsBackToExtension(string fileName, string expected)
    {
        Registry.TryResolveSourceType("application/octet-stream", fileName, out var sourceType).Should().BeTrue();
        sourceType.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolveSourceType_MissingContentType_FallsBackToExtension(string? contentType)
    {
        Registry.TryResolveSourceType(contentType, "report.pdf", out var sourceType).Should().BeTrue();
        sourceType.Should().Be("pdf");
    }

    [Theory]
    [InlineData("application/octet-stream", "archive.zip")]
    [InlineData("application/octet-stream", "notes.rtf")]
    [InlineData("application/octet-stream", "no-extension")]
    [InlineData("application/octet-stream", null)]
    [InlineData("image/png", "photo.pdf")] // a specific, unsupported MIME type is not overridden by the extension
    [InlineData("application/vnd.ms-outlook", "mail.msg")]
    public void TryResolveSourceType_Unsupported_ReturnsFalse(string contentType, string? fileName)
    {
        Registry.TryResolveSourceType(contentType, fileName, out var sourceType).Should().BeFalse();
        sourceType.Should().BeNull();
    }

    [Fact]
    public void TryResolveSourceType_MappedTypeWithoutExtractor_ReturnsFalse()
    {
        var pdfOnly = new TextExtractorRegistry([new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance)]);

        pdfOnly.TryResolveSourceType("application/octet-stream", "texts.csv", out _).Should().BeFalse();
    }
}
