// ============================================================
// Unit Tests — PdfReportRenderer
// Resolved through AddLindrakeReports(), which also sets the
// QuestPDF license the renderer depends on.
// ============================================================

using System.Text;
using FluentAssertions;
using Lindrake.Api.Extensions;
using Lindrake.Api.Services.Reports;
using Lindrake.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace Lindrake.UnitTests.Services.Reports;

public sealed class PdfReportRendererTests
{
    private static readonly IReportRenderer<byte[]> Renderer = new ServiceCollection()
        .AddLindrakeReports()
        .BuildServiceProvider()
        .GetRequiredService<IReportRenderer<byte[]>>();

    [Fact]
    public void Build_ReturnsNonEmptyPdf()
    {
        var document = new ReportDocument
        {
            Title = "Test Report",
            GeneratedAt = DateTimeOffset.UtcNow,
            Messages =
            [
                new ReportMessage { Role = "user", Content = "What is X?" },
                new ReportMessage { Role = "assistant", Content = "X is Y.", Citations = [] }
            ]
        };

        var pdf = Renderer.Render(document);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 100);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Build_RendersEachTurnWithItsSources_AndSkipsOtherRoles()
    {
        // Roles match case-insensitively; a turn with any other role is left out.
        // A source is labelled by title, else URL, else "(unknown)", and carries
        // its score to three decimals.
        var document = new ReportDocument
        {
            Title = "Byrne family",
            GeneratedAt = DateTimeOffset.UtcNow,
            Messages =
            [
                new ReportMessage { Role = "USER", Content = "Where was Michael born?" },
                new ReportMessage
                {
                    Role = "Assistant",
                    Content = "In Cork.",
                    Citations =
                    [
                        new SourceCitation { Type = "web", Title = "Parish register", Url = "https://example.test/reg", RelevanceScore = 0.92 },
                        new SourceCitation { Type = "web", Url = "https://example.test/census" },
                        new SourceCitation { Type = "document" }
                    ]
                },
                new ReportMessage { Role = "system", Content = "Hidden system turn" },
                new ReportMessage { Role = "user", Content = "And his wife?" }
            ]
        };

        var text = PdfText(Renderer.Render(document));

        text.Should().ContainAll("Query 1", "Where was Michael born?", "Answer", "In Cork.", "Query 2", "And his wife?");
        text.Should().NotContain("Hidden system turn");

        var sources = text[text.IndexOf("Sources:", StringComparison.Ordinal)..text.IndexOf("Query 2", StringComparison.Ordinal)];
        sources.Should().ContainAll("Parish register", "0.920", "example.test/census", "(unknown)");
        sources.Should().NotContain("example.test/reg", "a titled source shows its title, not its URL")
            .And.NotContain("score", "the PDF prints the bare score");
    }

    [Fact]
    public void Build_EmptyMessages_ReturnsValidPdf()
    {
        var document = new ReportDocument { Title = "Empty Report", GeneratedAt = DateTimeOffset.UtcNow, Messages = [] };

        var pdf = Renderer.Render(document);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 100);
    }

    /// <summary>
    /// The PDF's text, lines run together. The bullet, the dash and some ligatures
    /// ("tt") do not extract as written, so assertions stay on plain words.
    /// </summary>
    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Concat(document.GetPages().Select(page => page.Text));
    }
}
