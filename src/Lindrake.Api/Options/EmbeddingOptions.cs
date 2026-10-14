// ============================================================
// Lindrake.Api — Embedding Provider Configuration Options
// Selects between the paid OpenAI API and a local ONNX model.
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace Lindrake.Api.Options;

/// <summary>
/// Which embedding backend the API uses.
/// </summary>
public enum EmbeddingProvider
{
    /// <summary>Paid OpenAI API (text-embedding-3-large, 3072-dim). Default.</summary>
    OpenAi,

    /// <summary>Local ONNX sentence-embedding model via Microsoft.ML.OnnxRuntime.</summary>
    Onnx
}

/// <summary>
/// Sentence-pooling strategy applied to the model's token-level last_hidden_state.
/// MUST match the model's 1_Pooling/config.json or embeddings silently diverge
/// from the reference. bge-* uses <see cref="Cls"/>; all-MiniLM-* uses <see cref="Mean"/>.
/// </summary>
public enum PoolingStrategy
{
    /// <summary>Take the first ([CLS]) token's hidden state. Correct for the BGE family.</summary>
    Cls,

    /// <summary>Masked mean over all real tokens. Correct for the all-MiniLM family.</summary>
    Mean
}

/// <summary>
/// Strongly-typed options for embedding generation.
/// Bound from the "Embedding" configuration section.
/// </summary>
/// <remarks>
/// The local ONNX path expects a sentence-transformer model whose graph emits a
/// token-level last_hidden_state; the service applies masked mean-pooling +
/// L2-normalization. <see cref="QueryInstruction"/> is prepended to QUERY text only
/// (the single-text <c>GenerateEmbeddingAsync</c> call path), never to documents —
/// matching the bge family's asymmetric retrieval convention. Leave it empty for
/// symmetric models such as all-MiniLM-L6-v2.
/// </remarks>
public sealed class EmbeddingOptions
{
    public const string Section = "Embedding";

    /// <summary>Backend selector. Defaults to OpenAI to preserve existing behavior.</summary>
    public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.OpenAi;

    /// <summary>Filesystem path to the .onnx model (ONNX provider only).</summary>
    public string ModelPath { get; set; } = "models/bge-small-en-v1.5/model.onnx";

    /// <summary>
    /// Filesystem path to the WordPiece vocabulary file (vocab.txt) consumed by
    /// <c>BertTokenizer.Create</c> (ONNX provider only). Must be a <c>vocab.txt</c> file —
    /// NOT tokenizer.json; BertTokenizer.Create expects the raw WordPiece vocabulary list,
    /// not the HuggingFace tokenizer config JSON.
    /// </summary>
    public string TokenizerPath { get; set; } = "models/bge-small-en-v1.5/vocab.txt";

    /// <summary>Output embedding dimension. Must match the model AND the pgvector column.</summary>
    [Range(1, int.MaxValue)]
    public int Dimensions { get; set; } = 384;

    /// <summary>Max token sequence length; longer inputs are truncated before inference.</summary>
    [Range(1, int.MaxValue)]
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>Pooling strategy. Must match the model. bge-* = Cls (default), all-MiniLM-* = Mean.</summary>
    public PoolingStrategy Pooling { get; set; } = PoolingStrategy.Cls;

    /// <summary>
    /// Instruction prefix prepended to QUERY text only. bge-small-en-v1.5 expects
    /// "Represent this sentence for searching relevant passages: ". Empty = no prefix.
    /// </summary>
    public string QueryInstruction { get; set; } = "Represent this sentence for searching relevant passages: ";
}
