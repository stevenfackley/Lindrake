// ============================================================
// Unit Tests — ReportRequestFactory / ReportDocument
// ============================================================

using FluentAssertions;
using Lindrake.Api.Options;
using Lindrake.Api.Services.Reports;
using Lindrake.Shared.DTOs;

namespace Lindrake.UnitTests.Services.Reports;

public sealed class ReportRequestFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 5, TimeSpan.Zero);
    private readonly FixedTimeProvider _clock = new(Now);

    [Fact]
    public void From_MapsTurnsAndStampsTheClockTime()
    {
        var citation = new SourceCitation { Type = "web", Url = "https://example.test" };
        var request = new GenerateReportRequest
        {
            Title = "Byrne family",
            Messages =
            [
                new ReportTurn { Role = "user", Content = "Q" },
                new ReportTurn { Role = "assistant", Content = "A", Citations = [citation] }
            ]
        };

        var document = ReportRequestFactory.From(request, _clock, new ResearchProfileOptions());

        document.Title.Should().Be("Byrne family");
        document.GeneratedAt.Should().Be(Now);
        document.Timestamp.Should().Be("2026-06-21T12:00:05Z");
        document.FileName("pdf").Should().Be("report-20260621120005.pdf");
        document.Messages.Should().HaveCount(2);
        document.Messages[1].Citations.Should().Equal(citation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void From_BlankTitle_UsesTheProfileTitleTemplate(string? title)
    {
        var profile = new ResearchProfileOptions { Name = "Claims", ReportTitle = "{profile} review {date}" };
        var request = new GenerateReportRequest { Title = title, Messages = [new ReportTurn { Role = "user", Content = "Q" }] };

        ReportRequestFactory.From(request, _clock, profile).Title.Should().Be("Claims review 2026-06-21");
    }

    [Fact]
    public void From_BlankTitleWithDefaultProfile_IsResearchReport()
    {
        var request = new GenerateReportRequest { Messages = [new ReportTurn { Role = "user", Content = "Q" }] };

        ReportRequestFactory.From(request, _clock, new ResearchProfileOptions()).Title.Should().Be("Research Report");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
