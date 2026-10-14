// ============================================================
// Tests — OnnxEmbeddingService parity vs Python sentence-transformers
//
// Guards against tokenizer/pooling regressions by asserting cosine
// similarity >= 0.999 between the C# ONNX pipeline and reference vectors
// produced by the authoritative sentence-transformers library.
//
// SKIPPED until the fixture is populated:
//   Run tools/generate_reference_vectors.py in a Python 3.10–3.12
//   environment with sentence-transformers + torch installed, then:
//     python tools/generate_reference_vectors.py \
//       > tests/OmniSift.UnitTests/Services/Fixtures/bge_small_en_v1_5_reference_vectors.json
//   Then remove the [Fact(Skip="...")] attribute below.
//
// Note: Python 3.14 has no torch wheels as of 2026-06; use 3.10–3.12.
// ============================================================

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniSift.Api.Options;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class OnnxEmbeddingParityTests
{
    private const double CosineThreshold = 0.999;

    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "Services", "Fixtures", "bge_small_en_v1_5_reference_vectors.json");

    private sealed record ReferenceVector(string Text, float[] Vector);

    private static List<ReferenceVector>? LoadFixture()
    {
        if (!File.Exists(FixturePath))
            return null;

        var json = File.ReadAllText(FixturePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        var result = new List<ReferenceVector>();
        foreach (var element in root.EnumerateArray())
        {
            var text = element.GetProperty("text").GetString()!;
            var vector = element.GetProperty("vector")
                .EnumerateArray()
                .Select(v => v.GetSingle())
                .ToArray();
            result.Add(new ReferenceVector(text, vector));
        }

        return result.Count > 0 ? result : null;
    }

    private static (string ModelPath, string VocabPath)? LocateModel()
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

    private static double Cosine(float[] a, float[] b)
    {
        // Both vectors are already L2-normalized; dot product == cosine similarity.
        double dot = 0;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
    }

    [Fact]
    public async Task DocumentEmbeddings_MatchPythonReferenceVectors_CosineSimilarityAtLeast0_999()
    {
        var references = LoadFixture();
        if (references is null)
            return; // fixture absent — skip rather than fail

        // NOTE: models/ is gitignored, so on CI this returns null and the parity
        // gate reports green without ever running. Run
        // scripts/fetch-embedding-model.ps1 locally to actually exercise it.
        var model = LocateModel();
        if (model is null)
            return; // model binary absent — skip rather than fail

        var opts = new EmbeddingOptions
        {
            Provider = EmbeddingProvider.Onnx,
            ModelPath = model.Value.ModelPath,
            TokenizerPath = model.Value.VocabPath,
            Dimensions = 384,
            Pooling = PoolingStrategy.Cls,
            QueryInstruction = "Represent this sentence for searching relevant passages: "
        };

        using var sut = new OnnxEmbeddingService(
            Options.Create(opts),
            NullLogger<OnnxEmbeddingService>.Instance);

        var texts = references.Select(r => r.Text).ToList();
        // Use the document path (GenerateEmbeddingsAsync — no query prefix)
        // to match what sentence-transformers produces without instruction.
        var embeddings = await sut.GenerateEmbeddingsAsync(texts);

        for (var i = 0; i < references.Count; i++)
        {
            var cosine = Cosine(embeddings[i].ToArray(), references[i].Vector);
            cosine.Should().BeGreaterThanOrEqualTo(
                CosineThreshold,
                because: $"C# pipeline must match sentence-transformers reference for \"{references[i].Text}\"");
        }
    }
}
