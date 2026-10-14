// ============================================================
// Unit Tests — VectorSearchPlugin (adapter over IHybridSearchService)
// ============================================================

using System.Text.Json;
using FluentAssertions;
using Lindrake.Api.Plugins;
using Lindrake.Api.Services;
using Lindrake.Api.Services.Search;
using Lindrake.UnitTests.TestSupport;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lindrake.UnitTests.Plugins;

public sealed class VectorSearchPluginTests
{
    private readonly Mock<IHybridSearchService> _search = new();
    private readonly CitationAccumulator _citations = new();
    private readonly RecordingLogger<VectorSearchPlugin> _logger = new();

    [Fact]
    public async Task SearchDocuments_RecordsCitationsAndReturnsRankedJson()
    {
        var hit = Hit(vectorSimilarity: 0.87654, content: new string('x', 400));
        _search.Setup(s => s.SearchAsync("grandfather", 5, It.IsAny<CancellationToken>())).ReturnsAsync([hit]);

        var output = await Plugin().SearchDocumentsAsync("grandfather");

        var citation = _citations.GetCitations().Should().ContainSingle().Subject;
        citation.ChunkId.Should().Be(hit.ChunkId);
        citation.RelevanceScore.Should().Be(0.8765);
        citation.Snippet.Should().HaveLength(301).And.EndWith("…");

        using var json = JsonDocument.Parse(output);
        var first = json.RootElement[0];
        first.GetProperty("rank").GetInt32().Should().Be(1);
        first.GetProperty("fileName").GetString().Should().Be("letters.pdf");
        first.GetProperty("relevanceScore").GetDouble().Should().Be(0.8765);
    }

    [Fact]
    public async Task SearchDocuments_KeywordOnlyHit_UsesKeywordScore()
    {
        _search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Hit(vectorSimilarity: null, keywordSimilarity: 0.61)]);

        await Plugin().SearchDocumentsAsync("John Smith");

        _citations.GetCitations().Single().RelevanceScore.Should().Be(0.61);
    }

    [Fact]
    public async Task SearchDocuments_PassesTheKernelCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), cts.Token)).ReturnsAsync([]);

        var output = await Plugin().SearchDocumentsAsync("q", 3, cts.Token);

        output.Should().Be(VectorSearchPlugin.NoResultsMessage);
        _search.Verify(s => s.SearchAsync("q", 3, cts.Token), Times.Once);
    }

    [Fact]
    public async Task SearchDocuments_ClampsTopK()
    {
        _search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Plugin().SearchDocumentsAsync("q", 500);

        _search.Verify(s => s.SearchAsync("q", 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchDocuments_SearchFails_ReturnsGenericMessageAndLogsError()
    {
        _search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Npgsql: password authentication failed for user lindrake"));

        var output = await Plugin().SearchDocumentsAsync("q");

        output.Should().Be(VectorSearchPlugin.SearchUnavailableMessage).And.NotContain("password");
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
        _citations.GetCitations().Should().BeEmpty();
    }

    [Fact]
    public async Task SearchDocuments_RequestCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Plugin().SearchDocumentsAsync("q", 5, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchDocuments_DoesNotLogQueryText()
    {
        _search.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await Plugin().SearchDocumentsAsync("Jane Doe SSN 123-45-6789");

        _logger.Entries.Should().NotContain(e => e.Message.Contains("Jane", StringComparison.Ordinal));
    }

    private VectorSearchPlugin Plugin() => new(_search.Object, _citations, _logger);

    private static SearchHit Hit(double? vectorSimilarity, double? keywordSimilarity = null, string content = "text") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "letters.pdf", "pdf", 0, content, vectorSimilarity, keywordSimilarity, 0.03);
}
