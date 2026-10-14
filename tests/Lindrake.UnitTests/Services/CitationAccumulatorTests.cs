// ============================================================
// Unit Tests — CitationAccumulator
// Verifies deduplication, ordering, and per-type recording.
// ============================================================

using FluentAssertions;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class CitationAccumulatorTests
{
    private readonly CitationAccumulator _sut = new();

    // ── Document chunks ──────────────────────────────────────

    [Fact]
    public void AddDocumentChunk_SingleChunk_AppearsInCitations()
    {
        var id = Guid.NewGuid();
        var dsId = Guid.NewGuid();
        _sut.AddDocumentChunk(dsId, id, "report.pdf", "pdf", 0.9);

        var citations = _sut.GetCitations();
        citations.Should().HaveCount(1);
        citations[0].Type.Should().Be("document");
        citations[0].ChunkId.Should().Be(id);
        citations[0].DataSourceId.Should().Be(dsId);
        citations[0].Title.Should().Be("report.pdf");
        citations[0].RelevanceScore.Should().Be(0.9);
    }

    [Fact]
    public void AddDocumentChunk_SameChunkTwiceLowerScore_KeepsHigherScore()
    {
        var id = Guid.NewGuid();
        var dsId = Guid.NewGuid();
        _sut.AddDocumentChunk(dsId, id, "doc.pdf", "pdf", 0.8);
        _sut.AddDocumentChunk(dsId, id, "doc.pdf", "pdf", 0.5); // lower — ignored

        var citations = _sut.GetCitations();
        citations.Should().HaveCount(1);
        citations[0].RelevanceScore.Should().Be(0.8);
    }

    [Fact]
    public void AddDocumentChunk_SameChunkTwiceHigherScore_Upgrades()
    {
        var id = Guid.NewGuid();
        var dsId = Guid.NewGuid();
        _sut.AddDocumentChunk(dsId, id, "doc.pdf", "pdf", 0.5);
        _sut.AddDocumentChunk(dsId, id, "doc.pdf", "pdf", 0.95); // higher — wins

        var citations = _sut.GetCitations();
        citations.Should().HaveCount(1);
        citations[0].RelevanceScore.Should().Be(0.95);
    }

    [Fact]
    public void AddDocumentChunk_DifferentChunks_BothTracked()
    {
        _sut.AddDocumentChunk(Guid.NewGuid(), Guid.NewGuid(), "a.pdf", "pdf", 0.7);
        _sut.AddDocumentChunk(Guid.NewGuid(), Guid.NewGuid(), "b.pdf", "pdf", 0.6);

        _sut.GetCitations().Should().HaveCount(2);
    }

    // ── Web results ──────────────────────────────────────────

    [Fact]
    public void AddWebResult_SingleResult_AppearsInCitations()
    {
        _sut.AddWebResult("https://example.com", "Example Domain", 0.88, "A snippet");

        var citations = _sut.GetCitations();
        citations.Should().HaveCount(1);
        citations[0].Type.Should().Be("web");
        citations[0].Url.Should().Be("https://example.com");
        citations[0].Title.Should().Be("Example Domain");
        citations[0].RelevanceScore.Should().Be(0.88);
        citations[0].Snippet.Should().Be("A snippet");
    }

    [Fact]
    public void AddWebResult_SameUrlTwiceLowerScore_KeepsHigherScore()
    {
        _sut.AddWebResult("https://example.com", "X", 0.9);
        _sut.AddWebResult("https://example.com", "X", 0.4);

        _sut.GetCitations().Should().HaveCount(1);
        _sut.GetCitations()[0].RelevanceScore.Should().Be(0.9);
    }

    // ── Archived pages ───────────────────────────────────────

    [Fact]
    public void AddArchivedPage_Single_AppearsInCitations()
    {
        _sut.AddArchivedPage(
            "https://web.archive.org/web/20230101/https://example.com",
            "https://example.com",
            "20230101120000");

        var citations = _sut.GetCitations();
        citations.Should().HaveCount(1);
        citations[0].Type.Should().Be("archive");
        citations[0].Url.Should().Contain("archive.org");
        citations[0].Title.Should().Contain("example.com");
        citations[0].Title.Should().Contain("20230101120000");
    }

    [Fact]
    public void AddArchivedPage_SameUrlTwice_Deduplicated()
    {
        var archiveUrl = "https://web.archive.org/web/20230101/https://example.com";
        _sut.AddArchivedPage(archiveUrl, "https://example.com", "20230101");
        _sut.AddArchivedPage(archiveUrl, "https://example.com", "20230101");

        _sut.GetCitations().Should().HaveCount(1);
    }

    // ── Ordering ─────────────────────────────────────────────

    [Fact]
    public void GetCitations_OrderedByScoreDescending()
    {
        _sut.AddWebResult("https://low.com", "Low", 0.3);
        _sut.AddWebResult("https://high.com", "High", 0.95);
        _sut.AddWebResult("https://mid.com", "Mid", 0.65);

        var citations = _sut.GetCitations();
        citations[0].RelevanceScore.Should().Be(0.95);
        citations[1].RelevanceScore.Should().Be(0.65);
        citations[2].RelevanceScore.Should().Be(0.3);
    }

    [Fact]
    public void GetCitations_ArchiveAtEnd_BecauseNullScore()
    {
        _sut.AddWebResult("https://web.com", "Web", 0.8);
        _sut.AddArchivedPage("https://archive.org/...", "https://orig.com", null);

        var citations = _sut.GetCitations();
        citations[0].Type.Should().Be("web");
        citations[1].Type.Should().Be("archive");
    }

    // ── Mixed types don't collide ────────────────────────────

    [Fact]
    public void MixedTypes_AllTrackedSeparately()
    {
        _sut.AddDocumentChunk(Guid.NewGuid(), Guid.NewGuid(), "doc.pdf", "pdf", 0.9);
        _sut.AddWebResult("https://example.com", "Web", 0.8);
        _sut.AddArchivedPage("https://archive.org/...", "https://orig.com", "20230101");

        _sut.GetCitations().Should().HaveCount(3);
    }

    // ── Empty state ──────────────────────────────────────────

    [Fact]
    public void GetCitations_WhenEmpty_ReturnsEmptyList()
    {
        _sut.GetCitations().Should().BeEmpty();
    }
}
