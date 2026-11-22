// ============================================================
// Lindrake.Api — PDF report renderer (QuestPDF)
// The QuestPDF license is set once, in AddLindrakeReports().
// ============================================================

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lindrake.Api.Services.Reports;

/// <summary>Renders a <see cref="ReportDocument"/> as a PDF.</summary>
public sealed class PdfReportRenderer : IReportRenderer<byte[]>
{
    public const string ContentType = "application/pdf";

    public byte[] Render(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var timestamp = document.Timestamp;

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Title
                    col.Item().Text(document.Title)
                        .FontSize(22).Bold();

                    // Generated-at
                    col.Item().Text($"Generated: {timestamp}")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                    // Messages
                    var turnNumber = 0;
                    foreach (var msg in document.Messages)
                    {
                        if (msg.IsUser)
                        {
                            turnNumber++;
                            col.Item().PaddingTop(6).Text($"Query {turnNumber}")
                                .FontSize(14).Bold();
                            col.Item()
                                .BorderLeft(3).BorderColor(Colors.Blue.Lighten2)
                                .PaddingLeft(8)
                                .Text(msg.Content)
                                .FontSize(11).Italic();
                        }
                        else if (msg.IsAssistant)
                        {
                            col.Item().Text("Answer").FontSize(13).SemiBold();
                            col.Item().Text(msg.Content).FontSize(11);

                            if (msg.Citations.Count > 0)
                            {
                                col.Item().PaddingTop(4).Text("Sources:").FontSize(10).SemiBold();
                                foreach (var c in msg.Citations)
                                {
                                    var label = ReportCitations.Label(c);
                                    var score = ReportCitations.Score(c);
                                    var scoreStr = score is not null ? " — " + score : string.Empty;
                                    col.Item().PaddingLeft(12).Text($"• {label}{scoreStr}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken3);
                                }
                            }

                            col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                        }
                    }
                });
            });
        });

        return pdf.GeneratePdf();
    }
}
