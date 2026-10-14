// ============================================================
// Unit Tests — PdfReportRenderer
// Resolved through AddLindrakeReports(), which also sets the
// QuestPDF license the renderer depends on.
// ============================================================

using System.Text;
using Lindrake.Api.Extensions;
using Lindrake.Api.Services.Reports;
using Microsoft.Extensions.DependencyInjection;

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
    public void Build_EmptyMessages_ReturnsValidPdf()
    {
        var document = new ReportDocument { Title = "Empty Report", GeneratedAt = DateTimeOffset.UtcNow, Messages = [] };

        var pdf = Renderer.Render(document);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 100);
    }
}
