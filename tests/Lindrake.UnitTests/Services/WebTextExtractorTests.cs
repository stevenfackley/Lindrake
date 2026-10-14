// ============================================================
// Unit Tests — WebTextExtractor
// Verifies HTML text extraction and noise stripping
// ============================================================

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class WebTextExtractorTests
{
    private readonly WebTextExtractor _sut;

    public WebTextExtractorTests()
    {
        _sut = new WebTextExtractor(Mock.Of<ILogger<WebTextExtractor>>());
    }

    [Fact]
    public void SourceType_ReturnsWeb()
    {
        _sut.SourceType.Should().Be("web");
    }

    [Fact]
    public async Task ExtractText_SimpleHtml_ExtractsBodyText()
    {
        var html = "<html><body><p>Hello World</p></body></html>";
        var result = await ExtractFromString(html);

        result.Should().Contain("Hello World");
    }

    [Fact]
    public async Task ExtractText_WithTitle_IncludesTitle()
    {
        var html = "<html><head><title>Test Page</title></head><body><p>Content</p></body></html>";
        var result = await ExtractFromString(html);

        result.Should().Contain("[Title: Test Page]");
        result.Should().Contain("Content");
    }

    [Fact]
    public async Task ExtractText_StripsScripts()
    {
        var html = """
            <html><body>
                <p>Visible text</p>
                <script>var x = 'hidden';</script>
                <p>More visible text</p>
            </body></html>
            """;

        var result = await ExtractFromString(html);

        result.Should().Contain("Visible text");
        result.Should().Contain("More visible text");
        result.Should().NotContain("var x");
        result.Should().NotContain("hidden");
    }

    [Fact]
    public async Task ExtractText_StripsStyles()
    {
        var html = """
            <html><body>
                <style>.red { color: red; }</style>
                <p>Content here</p>
            </body></html>
            """;

        var result = await ExtractFromString(html);

        result.Should().Contain("Content here");
        result.Should().NotContain("color: red");
    }

    [Fact]
    public async Task ExtractText_StripsNavAndFooter()
    {
        var html = """
            <html><body>
                <nav><a href="/">Home</a></nav>
                <main><p>Main content</p></main>
                <footer>Copyright 2024</footer>
            </body></html>
            """;

        var result = await ExtractFromString(html);

        result.Should().Contain("Main content");
        result.Should().NotContain("Copyright 2024");
    }

    [Fact]
    public async Task ExtractText_PrefersArticleContent()
    {
        var html = """
            <html><body>
                <div class="sidebar">Sidebar stuff</div>
                <article>
                    <p>This is the main article content that is long enough to be selected as the primary content area for extraction purposes.</p>
                </article>
            </body></html>
            """;

        var result = await ExtractFromString(html);
        result.Should().Contain("main article content");
    }

    [Fact]
    public async Task ExtractText_DecodesHtmlEntities()
    {
        var html = "<html><body><p>Tom &amp; Jerry &lt;3</p></body></html>";
        var result = await ExtractFromString(html);

        result.Should().Contain("Tom & Jerry <3");
    }

    [Fact]
    public async Task ExtractText_EmptyHtml_ReturnsEmpty()
    {
        var result = await ExtractFromString(string.Empty);
        result.Should().BeEmpty();
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
        return await _sut.ExtractTextAsync(stream, "test.html");
    }
}
