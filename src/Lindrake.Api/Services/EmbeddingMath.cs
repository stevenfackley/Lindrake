// ============================================================
// OmniSift.Api — Embedding Math Helpers
// Pure, model-independent post-processing for sentence-embedding
// models: masked mean-pooling + L2-normalization.
// Extracted so the parity-critical math is unit-testable without
// loading the ONNX model.
// ============================================================

namespace OmniSift.Api.Services;

/// <summary>
/// Stateless numeric helpers shared by the ONNX embedding path.
/// </summary>
public static class EmbeddingMath
{
    /// <summary>
    /// Scales <paramref name="vector"/> to unit Euclidean length. A zero vector
    /// is returned unchanged (no divide-by-zero).
    /// </summary>
    public static float[] L2Normalize(ReadOnlySpan<float> vector)
    {
        double sumSquares = 0d;
        for (var i = 0; i < vector.Length; i++)
            sumSquares += (double)vector[i] * vector[i];

        var norm = Math.Sqrt(sumSquares);
        var result = new float[vector.Length];

        if (norm == 0d)
        {
            vector.CopyTo(result);
            return result;
        }

        for (var i = 0; i < vector.Length; i++)
            result[i] = (float)(vector[i] / norm);

        return result;
    }

    /// <summary>
    /// Masked mean-pool over a token-level last_hidden_state. Tokens whose
    /// attention-mask entry is 0 (padding) are excluded from the average, which
    /// is the difference between a correct sentence embedding and a corrupted one.
    /// </summary>
    /// <param name="lastHiddenState">Flattened [seqLen * hiddenSize] row-major.</param>
    /// <param name="attentionMask">[seqLen]; 1 = real token, 0 = padding.</param>
    /// <param name="hiddenSize">Embedding width (e.g. 384).</param>
    public static float[] MeanPool(ReadOnlySpan<float> lastHiddenState, ReadOnlySpan<int> attentionMask, int hiddenSize)
    {
        // (implementation below)
        return MeanPoolImpl(lastHiddenState, attentionMask, hiddenSize);
    }

    /// <summary>
    /// CLS-token pooling: returns the first token's hidden state. This is the
    /// correct sentence representation for the BGE family
    /// (1_Pooling/config.json: pooling_mode_cls_token = true), as opposed to
    /// mean-pooling used by the all-MiniLM family.
    /// </summary>
    public static float[] ClsPool(ReadOnlySpan<float> lastHiddenState, int hiddenSize)
        => lastHiddenState[..hiddenSize].ToArray();

    private static float[] MeanPoolImpl(ReadOnlySpan<float> lastHiddenState, ReadOnlySpan<int> attentionMask, int hiddenSize)
    {
        var sums = new double[hiddenSize];
        var realTokenCount = 0;

        for (var token = 0; token < attentionMask.Length; token++)
        {
            if (attentionMask[token] == 0)
                continue;

            realTokenCount++;
            var offset = token * hiddenSize;
            for (var dim = 0; dim < hiddenSize; dim++)
                sums[dim] += lastHiddenState[offset + dim];
        }

        var divisor = Math.Max(realTokenCount, 1);
        var pooled = new float[hiddenSize];
        for (var dim = 0; dim < hiddenSize; dim++)
            pooled[dim] = (float)(sums[dim] / divisor);

        return pooled;
    }
}
