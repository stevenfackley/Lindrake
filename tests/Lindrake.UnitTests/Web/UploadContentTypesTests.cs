using FluentAssertions;
using Moq;
using OmniSift.Api.Services;
using OmniSift.Api.Services.Extraction;
using OmniSift.Web.Services;

namespace OmniSift.UnitTests.Web;

public sealed class UploadContentTypesTests
{
    [Theory]
    [InlineData("report.pdf", null, "application/pdf")]
    [InlineData("REPORT.PDF", "application/octet-stream", "application/pdf")]
    [InlineData("texts.csv", "application/vnd.ms-excel", "text/csv")]
    [InlineData("export.json", null, "application/json")]
    [InlineData("page.htm", null, "text/html")]
    [InlineData("memo.docx", "", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("mail.eml", null, "message/rfc822")]
    [InlineData("box.mbox", null, "application/mbox")]
    [InlineData("notes.txt", null, "text/plain")]
    [InlineData("README.md", "", "text/markdown")]
    [InlineData("clipping.md", "application/octet-stream", "text/markdown")]
    [InlineData("noextension", null, "application/octet-stream")]
    [InlineData("image.png", "  ", "application/octet-stream")]
    public void ForFileName_PrefersTheExtension_ThenTheBrowserType(string fileName, string? browserType, string expected)
    {
        UploadContentTypes.ForFileName(fileName, browserType).Should().Be(expected);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".csv")]
    [InlineData(".json")]
    [InlineData(".html")]
    [InlineData(".htm")]
    [InlineData(".docx")]
    [InlineData(".eml")]
    [InlineData(".mbox")]
    [InlineData(".txt")]
    [InlineData(".md")]
    public void TheMimeTypeSent_ResolvesToTheSameSourceTypeAsTheExtension(string extension)
    {
        var registry = new TextExtractorRegistry(
            new[] { SourceTypes.Pdf, SourceTypes.Sms, SourceTypes.Web, SourceTypes.Docx, SourceTypes.Email, SourceTypes.Text }
                .Select(type => Mock.Of<ITextExtractor>(e => e.SourceType == type)));
        var fileName = "upload" + extension;

        registry.TryResolveSourceType(UploadContentTypes.ForFileName(fileName), fileName, out var byMime).Should().BeTrue();
        registry.TryResolveSourceType(UploadContentTypes.Fallback, fileName, out var byExtension).Should().BeTrue();
        byMime.Should().Be(byExtension);
    }
}
