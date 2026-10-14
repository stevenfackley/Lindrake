// ============================================================
// Tests — OnnxEmbeddingService (model-backed)
// Skips automatically if the bge model isn't present locally, so CI
// without the ~130 MB binary stays green. Run the fetch script to enable.
// ============================================================

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniSift.Api.Options;
using OmniSift.Api.Services;
using OmniSift.UnitTests.TestSupport;

namespace OmniSift.UnitTests.Services;

public sealed class OnnxEmbeddingServiceTests
{
    private static (string ModelPath, string TokenizerPath)? LocateModel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var root = Path.Combine(dir.FullName, "models", "bge-small-en-v1.5");
            var model = Path.Combine(root, "model.onnx");
            var vocab = Path.Combine(root, "vocab.txt");
            if (File.Exists(model) && File.Exists(vocab))
                return (model, vocab);
            dir = dir.Parent;
        }
        return null;
    }

    // Returns null when the model isn't present, so tests no-op instead of failing
    // CI that hasn't run scripts/fetch-embedding-model.ps1.
    private static OnnxEmbeddingService? CreateServiceOrNull(ILogger<OnnxEmbeddingService>? logger = null)
    {
        var located = LocateModel();
        if (located is null)
            return null;

        var opts = new EmbeddingOptions
        {
            Provider = EmbeddingProvider.Onnx,
            ModelPath = located.Value.ModelPath,
            TokenizerPath = located.Value.TokenizerPath,
            Dimensions = 384,
            Pooling = PoolingStrategy.Cls,
            QueryInstruction = "Represent this sentence for searching relevant passages: "
        };

        return new OnnxEmbeddingService(Options.Create(opts), logger ?? NullLogger<OnnxEmbeddingService>.Instance);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        for (var i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot; // both already L2-normalized
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_Returns384DimUnitVector()
    {
        using var sut = CreateServiceOrNull();
        if (sut is null) return; // model not fetched — skip

        var v = (await sut.GenerateEmbeddingAsync("How do I reset my password?")).ToArray();

        v.Should().HaveCount(384);
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        norm.Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_IsDeterministic()
    {
        using var sut = CreateServiceOrNull();
        if (sut is null) return;

        var a = (await sut.GenerateEmbeddingAsync("the quick brown fox")).ToArray();
        var b = (await sut.GenerateEmbeddingAsync("the quick brown fox")).ToArray();

        a.Should().Equal(b);
    }

    [Fact]
    public async Task Embeddings_RankSemanticallyRelatedTextHigher()
    {
        using var sut = CreateServiceOrNull();
        if (sut is null) return;

        // Query path (prefixed) vs document path (no prefix) — the real asymmetric usage.
        var query = (await sut.GenerateEmbeddingAsync("How do I reset my forgotten password?")).ToArray();

        var docs = await sut.GenerateEmbeddingsAsync(new[]
        {
            "Follow these steps to recover access if you forgot your login credentials.", // related
            "The mitochondria is the powerhouse of the cell."                              // unrelated
        });

        var related = Cosine(query, docs[0].ToArray());
        var unrelated = Cosine(query, docs[1].ToArray());

        // If CLS-pooling, the query prefix, or tokenization were wrong, this ordering
        // would not hold reliably — this is the de-facto correctness gate for the pipeline.
        related.Should().BeGreaterThan(unrelated);
    }

    [Fact]
    public async Task QueryPrefix_ChangesTheQueryEmbeddingVsRawDocumentEmbedding()
    {
        using var sut = CreateServiceOrNull();
        if (sut is null) return;

        const string text = "annual shareholder meeting minutes";
        var asQuery = (await sut.GenerateEmbeddingAsync(text)).ToArray();          // prefixed
        var asDocument = (await sut.GenerateEmbeddingsAsync(new[] { text }))[0].ToArray(); // raw

        // The bge query instruction must actually be applied on the query path only,
        // so the two embeddings of the same text are not identical.
        asQuery.Should().NotEqual(asDocument);
    }

    [Fact]
    public void DefaultChunks_FitTheModelWindow()
    {
        using var sut = CreateServiceOrNull();
        if (sut is null) return;

        var text = string.Join(" ", Enumerable.Repeat(
            "Quarterly revenue grew 12% (year-over-year), driven by cloud-infrastructure demand.", 300));
        var chunks = new TextChunker(sut, Options.Create(new ChunkingOptions())).ChunkText(text);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().AllSatisfy(c => sut.Count(c.Content).Should().BeLessThanOrEqualTo(480));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_OverlongInput_LogsTruncation()
    {
        var logger = new RecordingLogger<OnnxEmbeddingService>();
        using var sut = CreateServiceOrNull(logger);
        if (sut is null) return;

        await sut.GenerateEmbeddingsAsync([string.Join(' ', Enumerable.Repeat("word", 1000))]);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("truncated"));
    }
}
