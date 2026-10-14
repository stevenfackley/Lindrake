// ============================================================
// Unit Tests — TextChunker
// Token budget, overlap, sentence packing and edge cases. A
// one-token-per-word counter keeps the arithmetic obvious.
// ============================================================

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OmniSift.Api.Options;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class TextChunkerTests
{
    private static TextChunker Create(int maxTokens = 480, int overlapTokens = 48, ITokenCounter? counter = null) =>
        new(counter ?? new OneTokenPerWord(),
            Options.Create(new ChunkingOptions { MaxTokens = maxTokens, OverlapTokens = overlapTokens }));

    private static string Words(int count, string prefix = "w") =>
        string.Join(' ', Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

    [Fact]
    public void ChunkText_WithEmptyString_ReturnsEmptyList()
    {
        Create().ChunkText(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithWhitespace_ReturnsEmptyList()
    {
        Create().ChunkText("   \n\t  ").Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_WithNull_ThrowsArgumentNullException()
    {
        var act = () => Create().ChunkText(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var result = Create().ChunkText("Hello world this is a test");

        result.Should().ContainSingle();
        result[0].Content.Should().Be("Hello world this is a test");
        result[0].Index.Should().Be(0);
        result[0].TokenCount.Should().Be(6);
    }

    [Fact]
    public void ChunkText_ExactlyTheBudget_ReturnsSingleChunk()
    {
        var result = Create(maxTokens: 100, overlapTokens: 10).ChunkText(Words(100));

        result.Should().ContainSingle().Which.TokenCount.Should().Be(100);
    }

    [Fact]
    public void ChunkText_NoChunkExceedsTheTokenBudget()
    {
        // Mixed sentence lengths, some longer than the budget on their own.
        var text = string.Join(" ", Enumerable.Range(1, 60).Select(i => Words(i % 17 + 1, $"s{i}w") + "."));
        var counter = new OneTokenPerWord();

        var result = Create(maxTokens: 25, overlapTokens: 5, counter).ChunkText(text);

        result.Should().HaveCountGreaterThan(1);
        result.Should().AllSatisfy(c =>
        {
            c.TokenCount.Should().BeLessThanOrEqualTo(25);
            counter.Count(c.Content).Should().Be(c.TokenCount);
        });
    }

    [Fact]
    public void ChunkText_PacksWholeSentences()
    {
        // Ten 5-word sentences, budget 12, no overlap → two sentences per chunk.
        var text = string.Join(" ", Enumerable.Range(1, 10).Select(i => $"Sentence {i} has five words."));

        var result = Create(maxTokens: 12, overlapTokens: 0).ChunkText(text);

        result.Should().HaveCount(5);
        result.Should().AllSatisfy(c =>
        {
            c.TokenCount.Should().Be(10);
            c.Content.Should().EndWith(".");
        });
    }

    [Fact]
    public void ChunkText_ConsecutiveChunksOverlap()
    {
        var result = Create(maxTokens: 100, overlapTokens: 20).ChunkText(Words(1000));

        result.Should().HaveCountGreaterThan(1);
        for (var i = 0; i < result.Count - 1; i++)
        {
            var current = result[i].Content.Split(' ');
            var next = result[i + 1].Content.Split(' ');

            current.TakeLast(20).Should().Equal(next.Take(20), $"chunk {i} should overlap chunk {i + 1}");
        }
    }

    [Fact]
    public void ChunkText_IndicesAreSequential()
    {
        var result = Create(maxTokens: 50, overlapTokens: 5).ChunkText(Words(2000));

        result.Select(c => c.Index).Should().Equal(Enumerable.Range(0, result.Count));
    }

    [Fact]
    public void ChunkText_AllContentPreserved()
    {
        var originalWords = Enumerable.Range(1, 100).Select(i => $"word{i}").ToArray();

        var result = Create(maxTokens: 30, overlapTokens: 5).ChunkText(string.Join(' ', originalWords));

        var chunkWords = result.SelectMany(c => c.Content.Split(' ')).ToHashSet();
        chunkWords.Should().Contain(originalWords);
    }

    [Fact]
    public void ChunkText_SingleWordOverBudget_BecomesItsOwnChunk()
    {
        // One "word" of 50 tokens against a budget of 20 cannot be split further;
        // the chunker must emit it alone and keep going.
        var counter = new CharsAsTokens();
        var text = "short " + new string('x', 50) + " tail";

        var result = Create(maxTokens: 20, overlapTokens: 4, counter).ChunkText(text);

        result.Select(c => c.Content).Should().Equal("short", new string('x', 50), "tail");
    }

    [Fact]
    public void ChunkingOptions_Defaults_AreValid()
    {
        Validate(new ChunkingOptions()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(100, 150)]
    [InlineData(8, 0)]
    public void ChunkingOptions_Invalid_AreRejected(int maxTokens, int overlapTokens)
    {
        Validate(new ChunkingOptions { MaxTokens = maxTokens, OverlapTokens = overlapTokens })
            .Should().NotBeEmpty();
    }

    private static List<ValidationResult> Validate(ChunkingOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    private sealed class OneTokenPerWord : ITokenCounter
    {
        public int Count(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private sealed class CharsAsTokens : ITokenCounter
    {
        public int Count(string text) => text.Length;
    }
}
