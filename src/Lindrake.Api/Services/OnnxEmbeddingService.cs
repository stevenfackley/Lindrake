// ============================================================
// Lindrake.Api — Local ONNX Embedding Service
// Implements IEmbeddingService using a local sentence-embedding
// model via Microsoft.ML.OnnxRuntime — no cloud API, no per-token cost.
// ============================================================

using Lindrake.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Pgvector;

namespace Lindrake.Api.Services;

/// <summary>
/// Generates embeddings locally with an ONNX sentence-transformer model.
/// The model graph emits a token-level last_hidden_state; this service applies
/// the configured pooling (<see cref="PoolingStrategy"/>) + L2-normalization.
/// Holds a single thread-safe <c>InferenceSession</c> for the process lifetime,
/// so it is registered as a singleton. Also the <see cref="ITokenCounter"/> for the
/// ONNX provider, so chunks are sized with the model's own WordPiece tokenizer.
/// </summary>
/// <remarks>
/// Fail-fast: the constructor throws if the model/vocab are missing or the model's
/// output width disagrees with <see cref="EmbeddingOptions.Dimensions"/>, so a
/// misconfigured ONNX provider refuses to start rather than silently degrading.
/// <para>
/// Path resolution: relative <see cref="EmbeddingOptions.ModelPath"/> /
/// <see cref="EmbeddingOptions.TokenizerPath"/> values are resolved against
/// <see cref="IWebHostEnvironment.ContentRootPath"/> when the environment is
/// available (the normal DI path), or against the process working directory when
/// it is not (unit-test path where absolute paths are always supplied).
/// Absolute paths are passed through unchanged in both cases.
/// </para>
/// </remarks>
public sealed class OnnxEmbeddingService : IEmbeddingService, ITokenCounter, IDisposable
{
    private readonly EmbeddingOptions _opts;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly List<string> _inputNames;
    private readonly string _outputName;

    /// <summary>
    /// DI constructor — resolves relative model paths against
    /// <paramref name="env"/>.ContentRootPath so Docker images can ship a
    /// model at a well-known path (e.g. /app/models/…) regardless of CWD.
    /// </summary>
    public OnnxEmbeddingService(
        IOptions<EmbeddingOptions> options,
        ILogger<OnnxEmbeddingService> logger,
        IWebHostEnvironment env)
        : this(options, logger, env.ContentRootPath) { }

    /// <summary>
    /// Test-friendly constructor — pass absolute paths via
    /// <see cref="EmbeddingOptions"/> and omit the environment.
    /// Relative paths fall back to the process working directory (same
    /// behaviour as before this change), so existing tests are unaffected.
    /// </summary>
    public OnnxEmbeddingService(IOptions<EmbeddingOptions> options, ILogger<OnnxEmbeddingService> logger)
        : this(options, logger, contentRootPath: null) { }

    private OnnxEmbeddingService(
        IOptions<EmbeddingOptions> options,
        ILogger<OnnxEmbeddingService> logger,
        string? contentRootPath)
    {
        _opts = options.Value;
        _logger = logger;

        var modelPath = ResolvePath(_opts.ModelPath, contentRootPath);
        var vocabPath = ResolvePath(_opts.TokenizerPath, contentRootPath);

        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX embedding model not found at '{modelPath}'. Run scripts/fetch-embedding-model.ps1 or set Embedding:ModelPath.",
                modelPath);
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException(
                $"Tokenizer vocab not found at '{vocabPath}'. Set Embedding:TokenizerPath to the model's vocab.txt.",
                vocabPath);

        // Single-threaded inference — let ASP.NET Core handle request-level parallelism
        // rather than letting ORT spawn all-core threads per request (which causes contention
        // under concurrent HTTP load). ORT_ENABLE_ALL applies graph-level optimizations
        // (constant folding, node fusion) at session init time.
        var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        _session = new InferenceSession(modelPath, sessionOptions);
        sessionOptions.Dispose();

        _inputNames = [.. _session.InputMetadata.Keys];

        // Prefer the canonical output name; fall back to first key with a warning so
        // models that rename the output (e.g. "sentence_embedding") still work rather
        // than silently returning wrong data.
        if (_session.OutputMetadata.ContainsKey("last_hidden_state"))
        {
            _outputName = "last_hidden_state";
        }
        else
        {
            _outputName = _session.OutputMetadata.Keys.First();
            logger.LogWarning(
                "ONNX model does not have a 'last_hidden_state' output; using '{Output}' instead. " +
                "Verify this is the token-level hidden state, not a pooled/CLS output.",
                _outputName);
        }

        using (var vocab = File.OpenRead(vocabPath))
            _tokenizer = BertTokenizer.Create(vocab);

        // Fail-fast on a model/column dimension mismatch (e.g. a 768-dim model
        // pointed at a 384-dim pgvector column).
        var outDims = _session.OutputMetadata[_outputName].Dimensions;
        var hidden = outDims.Length > 0 ? outDims[^1] : -1;
        if (hidden > 0 && hidden != _opts.Dimensions)
            throw new InvalidOperationException(
                $"ONNX model output width {hidden} != configured Embedding:Dimensions {_opts.Dimensions}.");

        if (hidden <= 0)
            logger.LogWarning(
                "ONNX model output '{Output}' has a symbolic/dynamic last dimension; " +
                "dimension validation was skipped. Ensure the model emits {Dim}-dim vectors.",
                _outputName, _opts.Dimensions);

        logger.LogInformation(
            "ONNX embeddings ready: {Model} dim={Dim} pooling={Pooling} inputs=[{Inputs}] output={Output}",
            Path.GetFileName(modelPath), _opts.Dimensions, _opts.Pooling, string.Join(",", _inputNames), _outputName);
    }

    /// <summary>Single-text path = QUERY path; the instruction prefix is applied here only.</summary>
    public Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var prefixed = string.IsNullOrEmpty(_opts.QueryInstruction) ? text : _opts.QueryInstruction + text;
        return Task.Run(() => Embed(prefixed), cancellationToken);
    }

    /// <summary>Batch path = DOCUMENT path; no instruction prefix (bge convention).</summary>
    public Task<IReadOnlyList<Vector>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var list = texts.ToList();
        if (list.Count == 0)
            return Task.FromResult<IReadOnlyList<Vector>>([]);

        return Task.Run(() =>
        {
            var results = new List<Vector>(list.Count);
            foreach (var t in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(Embed(t));
            }
            return (IReadOnlyList<Vector>)results;
        }, cancellationToken);
    }

    /// <summary>WordPiece token count of <paramref name="text"/> (ITokenCounter).</summary>
    public int Count(string text) => _tokenizer.CountTokens(text);

    private Vector Embed(string text)
    {
        // BertTokenizer adds [CLS]…[SEP]; truncate to the model's max sequence length.
        var ids = _tokenizer.EncodeToIds(text);
        if (ids.Count > _opts.MaxSequenceLength)
        {
            // The dropped tail is invisible to search. TextChunker sizes chunks to
            // avoid this; seeing it means an oversized chunk or a misconfiguration.
            _logger.LogWarning(
                "Embedding input truncated from {TokenCount} to {MaxSequenceLength} tokens",
                ids.Count, _opts.MaxSequenceLength);
        }

        var count = Math.Min(ids.Count, _opts.MaxSequenceLength);

        var inputIds = new long[count];
        var attentionMask = new long[count];
        var tokenTypeIds = new long[count];
        for (var i = 0; i < count; i++)
        {
            inputIds[i] = ids[i];
            attentionMask[i] = 1L;
            tokenTypeIds[i] = 0L;
        }

        var shape = new[] { 1, count };
        var inputs = new List<NamedOnnxValue>(_inputNames.Count);
        foreach (var name in _inputNames)
        {
            var data = name switch
            {
                "attention_mask" => attentionMask,
                "token_type_ids" => tokenTypeIds,
                _ => inputIds
            };
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(data, shape)));
        }

        using var outputs = _session.Run(inputs);
        var lastHidden = outputs.First(o => o.Name == _outputName).AsTensor<float>().ToArray();

        var hidden = _opts.Dimensions;
        var pooled = _opts.Pooling == PoolingStrategy.Cls
            ? EmbeddingMath.ClsPool(lastHidden, hidden)
            : EmbeddingMath.MeanPool(lastHidden, OnesMask(count), hidden);

        return new Vector(EmbeddingMath.L2Normalize(pooled));
    }

    private static int[] OnesMask(int count)
    {
        var mask = new int[count];
        Array.Fill(mask, 1);
        return mask;
    }

    /// <summary>
    /// Resolves a model path to an absolute path.
    /// If <paramref name="path"/> is already absolute it is returned unchanged.
    /// If <paramref name="contentRootPath"/> is supplied (DI path), relative paths
    /// are anchored there (e.g. /app inside the container).
    /// Otherwise they fall back to <see cref="Path.GetFullPath(string)"/> which uses CWD.
    /// </summary>
    private static string ResolvePath(string path, string? contentRootPath)
    {
        if (Path.IsPathRooted(path))
            return path;

        return contentRootPath is not null
            ? Path.GetFullPath(path, contentRootPath)
            : Path.GetFullPath(path);
    }

    public void Dispose() => _session.Dispose();
}
