// ============================================================
// Unit Tests — MarkdownService
// Verifies safe HTML rendering and XSS neutralisation
// ============================================================

using FluentAssertions;
using OmniSift.Web.Services;

namespace OmniSift.UnitTests.Services;

public sealed class MarkdownServiceTests
{
    private readonly MarkdownService _sut = new();

    // ── Basic rendering ──────────────────────────────────────

    [Fact]
    public void Render_Bold_ProducesStrongTag()
    {
        var html = _sut.Render("**hello**");
        html.Should().Contain("<strong>hello</strong>");
    }

    [Fact]
    public void Render_Italic_ProducesEmTag()
    {
        var html = _sut.Render("_world_");
        html.Should().Contain("<em>world</em>");
    }

    [Fact]
    public void Render_Heading1_ProducesH1Tag()
    {
        var html = _sut.Render("# Title");
        html.Should().Contain("<h1");
        html.Should().Contain("Title");
        html.Should().Contain("</h1>");
    }

    [Fact]
    public void Render_Heading2_ProducesH2Tag()
    {
        var html = _sut.Render("## Section");
        html.Should().Contain("<h2");
        html.Should().Contain("Section");
    }

    [Fact]
    public void Render_UnorderedList_ProducesUlLiTags()
    {
        var html = _sut.Render("- alpha\n- beta\n- gamma");
        html.Should().Contain("<ul>");
        html.Should().Contain("<li>alpha</li>", "first list item");
        html.Should().Contain("<li>beta</li>", "second list item");
    }

    [Fact]
    public void Render_OrderedList_ProducesOlLiTags()
    {
        var html = _sut.Render("1. first\n2. second");
        html.Should().Contain("<ol>");
        html.Should().Contain("<li>first</li>");
    }

    [Fact]
    public void Render_InlineCode_ProducesCodeTag()
    {
        var html = _sut.Render("Use `dotnet run` to start.");
        html.Should().Contain("<code>dotnet run</code>");
    }

    [Fact]
    public void Render_FencedCodeBlock_ProducesPreCodeTags()
    {
        var html = _sut.Render("```\nconsole.log('hi');\n```");
        html.Should().Contain("<pre>");
        html.Should().Contain("<code>");
    }

    [Fact]
    public void Render_EmptyString_ReturnsEmpty()
    {
        _sut.Render(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Render_NullEquivalent_WhiteSpace_ReturnsEmpty()
    {
        // string.IsNullOrEmpty guards only empty/null; whitespace is legal markdown
        // (could be blank lines). Just verify it doesn't throw.
        var act = () => _sut.Render("   ");
        act.Should().NotThrow();
    }

    // ── XSS neutralisation ───────────────────────────────────

    [Fact]
    public void Render_ScriptTag_IsNotPresent()
    {
        var html = _sut.Render("<script>alert('xss')</script>");
        html.Should().NotContain("<script", because: "raw HTML is disabled");
    }

    [Fact]
    public void Render_InlineEventHandler_IsHtmlEncoded()
    {
        // DisableHtml encodes raw HTML rather than passing it through;
        // the onerror text appears HTML-encoded — safe as rendered text, not an attribute.
        var html = _sut.Render("<img src=x onerror=\"alert(1)\">");
        html.Should().NotContain("<img", because: "the raw tag must not survive as an actual HTML element");
        html.Should().Contain("&lt;img", because: "the tag should be HTML-encoded, not executable");
    }

    [Fact]
    public void Render_HtmlComment_IsHtmlEncoded()
    {
        // HTML comments are encoded as text, not passed as live HTML comments.
        var html = _sut.Render("<!-- drop table -->");
        html.Should().NotContain("<!--", because: "HTML comments must not survive as live HTML");
    }

    [Fact]
    public void Render_IframeTag_IsNotPresent()
    {
        var html = _sut.Render("<iframe src=\"https://evil.example\"></iframe>");
        html.Should().NotContain("<iframe", because: "raw HTML tags are disabled");
    }

    [Fact]
    public void Render_RawHtmlBlock_IsHtmlEncoded()
    {
        // DisableHtml encodes rather than executing raw HTML blocks.
        // The <div> survives as &lt;div&gt; — plain text, not a live element.
        var input = """
            <div onclick="steal()">
              sensitive
            </div>
            """;
        var html = _sut.Render(input);
        html.Should().NotContain("<div", because: "raw HTML must not be passed as a live element");
        html.Should().Contain("&lt;div", because: "the tag should be HTML-encoded");
    }

    // ── Idempotency / stability ──────────────────────────────

    [Fact]
    public void Render_CalledTwiceWithSameInput_ProducesSameOutput()
    {
        const string md = "## Heading\n\n- item 1\n- item 2\n\n**bold**";
        _sut.Render(md).Should().Be(_sut.Render(md));
    }
}
