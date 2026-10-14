// ============================================================
// Unit Tests — EmbeddingMath
// Parity-critical post-processing for the local ONNX embedder.
// ============================================================

using FluentAssertions;
using OmniSift.Api.Services;

namespace OmniSift.UnitTests.Services;

public sealed class EmbeddingMathTests
{
    [Fact]
    public void L2Normalize_ScalesVectorToUnitLength()
    {
        // 3-4-5 right triangle → normalized = [0.6, 0.8], magnitude exactly 1.
        var result = EmbeddingMath.L2Normalize(new float[] { 3f, 4f });

        result[0].Should().BeApproximately(0.6f, 1e-6f);
        result[1].Should().BeApproximately(0.8f, 1e-6f);

        var magnitude = MathF.Sqrt((result[0] * result[0]) + (result[1] * result[1]));
        magnitude.Should().BeApproximately(1f, 1e-6f);
    }

    [Fact]
    public void MeanPool_ExcludesPaddingTokensFromTheAverage()
    {
        // 3 tokens, hidden size 2. The third token is padding (mask=0) and carries
        // a wildly different value — if it leaked into the mean, the result would be
        // ~[34.3, 34.3] instead of the masked mean of the two real tokens, [1.5, 1.5].
        var lastHiddenState = new float[] { 1f, 1f, 2f, 2f, 100f, 100f };
        var attentionMask = new[] { 1, 1, 0 };

        var pooled = EmbeddingMath.MeanPool(lastHiddenState, attentionMask, hiddenSize: 2);

        pooled.Should().HaveCount(2);
        pooled[0].Should().BeApproximately(1.5f, 1e-6f);
        pooled[1].Should().BeApproximately(1.5f, 1e-6f);
    }

    [Fact]
    public void ClsPool_ReturnsFirstTokenHiddenStateOnly()
    {
        // BGE uses CLS pooling: the sentence embedding is token 0's hidden state.
        // Tokens 1 and 2 must be ignored entirely (no averaging).
        var lastHiddenState = new float[] { 10f, 20f, 1f, 1f, 2f, 2f };

        var pooled = EmbeddingMath.ClsPool(lastHiddenState, hiddenSize: 2);

        pooled.Should().Equal(10f, 20f);
    }
}
