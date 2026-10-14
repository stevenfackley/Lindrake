// ============================================================
// OmniSift.Api — Ingestion Pipeline
// Extract → PII scan → chunk → embed → persist for one DataSource.
// Shared by the background worker (uploads) and the synchronous
// web-ingestion endpoint.
// ============================================================

using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Api.Services.Extraction;

namespace OmniSift.Api.Services.Ingestion;

/// <summary>Runs the ingestion pipeline for a single <see cref="DataSource"/>.</summary>
public interface IIngestionPipeline
{
    /// <summary>
    /// Ingests <paramref name="content"/> into <paramref name="dataSource"/> — an existing
    /// row tracked by the scope's DbContext and owned by the scope's tenant.
    /// <para>
    /// Pipeline failures are not thrown: the row is committed as
    /// <see cref="IngestionStatus.Failed"/> with a client-safe message (the exception is
    /// logged) and returned. Cancellation also commits Failed, then rethrows.
    /// </para>
    /// </summary>
    /// <returns>The DataSource as persisted (Completed or Failed).</returns>
    Task<DataSource> RunAsync(DataSource dataSource, Stream content, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IIngestionPipeline"/>. Extraction, PII scan, chunking and
/// embedding touch no database state; the chunk inserts and the Completed status
/// are written by a single <c>SaveChanges</c>, i.e. one transaction, so they
/// commit together or not at all. Failure handling clears the change tracker and
/// writes only the Failed status, outside that transaction.
/// </summary>
public sealed class IngestionPipeline(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    ITextExtractorRegistry extractors,
    ITextChunker chunker,
    IEmbeddingService embeddingService,
    ILogger<IngestionPipeline> logger) : IIngestionPipeline
{
    /// <summary>Texts per embedding call.</summary>
    private const int EmbeddingBatchSize = 20;

    /// <inheritdoc />
    public async Task<DataSource> RunAsync(
        DataSource dataSource,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(content);

        // Defense in depth on top of RLS: never write chunks for another tenant.
        var tenantId = tenantContext.TenantId;
        if (dataSource.TenantId != tenantId)
            throw new InvalidOperationException(
                $"DataSource {dataSource.Id} does not belong to the scope's tenant.");

        logger.LogInformation(
            "Ingesting DataSource {DataSourceId}, type={SourceType}, tenant={TenantId}",
            dataSource.Id, dataSource.SourceType, tenantId);

        // Client-safe description of the stage in progress; reported if it throws.
        var stage = IngestionFailures.UnsupportedSourceType;
        try
        {
            if (!extractors.TryGet(dataSource.SourceType, out var extractor))
                return await MarkFailedAsync(dataSource, stage, exception: null).ConfigureAwait(false);

            stage = IngestionFailures.ExtractionFailed;
            var rawText = await extractor
                .ExtractTextAsync(content, dataSource.FileName, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(rawText))
                return await MarkFailedAsync(dataSource, IngestionFailures.NoExtractableText, exception: null)
                    .ConfigureAwait(false);

            logger.LogDebug("Extracted {Length} chars from DataSource {DataSourceId}", rawText.Length, dataSource.Id);

            stage = IngestionFailures.ChunkingFailed;
            var piiMatches = PiiScanner.Scan(rawText);
            var chunks = chunker.ChunkText(rawText);
            logger.LogDebug("Created {ChunkCount} chunks from DataSource {DataSourceId}", chunks.Count, dataSource.Id);

            stage = IngestionFailures.EmbeddingFailed;
            var embeddings = await EmbedAsync(chunks, dataSource.Id, cancellationToken).ConfigureAwait(false);

            stage = IngestionFailures.PersistenceFailed;
            var documentChunks = chunks.Select((chunk, idx) => new DocumentChunk
            {
                TenantId = tenantId,
                DataSourceId = dataSource.Id,
                Content = chunk.Content,
                ChunkIndex = chunk.Index,
                TokenCount = chunk.TokenCount,
                Embedding = embeddings[idx],
                Metadata = new Dictionary<string, object>
                {
                    ["source_type"] = dataSource.SourceType,
                    ["file_name"] = dataSource.FileName ?? string.Empty
                }
            }).ToList();

            dbContext.DocumentChunks.AddRange(documentChunks);

            // PII scan is flag-only — it never rejects a document.
            dataSource.HasPii = piiMatches.Count > 0;
            dataSource.PiiFlags = piiMatches.Count > 0
                ? [.. piiMatches.Select(m => m.Type.ToString())]
                : null;
            dataSource.Status = IngestionStatus.Completed;
            dataSource.ErrorMessage = null;
            dataSource.Metadata = new Dictionary<string, object>
            {
                ["chunk_count"] = documentChunks.Count,
                ["total_tokens"] = documentChunks.Sum(c => c.TokenCount),
                ["text_length"] = rawText.Length
            };

            // One SaveChanges = one transaction (chunks + Completed status); the
            // retrying execution strategy replays it as a unit on transient errors.
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Ingestion completed for DataSource {DataSourceId}: {ChunkCount} chunks, {TokenCount} tokens",
                dataSource.Id, documentChunks.Count, documentChunks.Sum(c => c.TokenCount));

            return dataSource;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkFailedAsync(dataSource, IngestionFailures.Cancelled, exception: null).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            return await MarkFailedAsync(dataSource, stage, ex).ConfigureAwait(false);
        }
    }

    private async Task<List<Pgvector.Vector>> EmbedAsync(
        IReadOnlyList<TextChunk> chunks,
        Guid dataSourceId,
        CancellationToken cancellationToken)
    {
        var embeddings = new List<Pgvector.Vector>(chunks.Count);

        for (var i = 0; i < chunks.Count; i += EmbeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = chunks.Skip(i).Take(EmbeddingBatchSize).Select(c => c.Content);
            embeddings.AddRange(await embeddingService
                .GenerateEmbeddingsAsync(batch, cancellationToken)
                .ConfigureAwait(false));

            logger.LogDebug(
                "Embedded {Done}/{Total} chunks for DataSource {DataSourceId}",
                Math.Min(i + EmbeddingBatchSize, chunks.Count), chunks.Count, dataSourceId);
        }

        if (embeddings.Count != chunks.Count)
            throw new InvalidOperationException(
                $"Embedding service returned {embeddings.Count} vectors for {chunks.Count} chunks.");

        return embeddings;
    }

    /// <summary>
    /// Discards everything the failed attempt staged (pending chunk inserts, a
    /// half-applied Completed status), reloads the row and commits only the Failed
    /// status. Never throws: if even that write fails it is logged and the in-memory
    /// entity is returned marked Failed.
    /// </summary>
    private async Task<DataSource> MarkFailedAsync(DataSource dataSource, string reason, Exception? exception)
    {
        if (exception is null)
            logger.LogWarning("Ingestion failed for DataSource {DataSourceId}: {Reason}", dataSource.Id, reason);
        else
            logger.LogError(exception, "Ingestion failed for DataSource {DataSourceId}: {Reason}", dataSource.Id, reason);

        dbContext.ChangeTracker.Clear();

        try
        {
            // CancellationToken.None: the failure must be recorded even when the
            // request or host is being cancelled.
            var current = await dbContext.DataSources
                .FirstOrDefaultAsync(ds => ds.Id == dataSource.Id, CancellationToken.None)
                .ConfigureAwait(false);

            if (current is null)
            {
                logger.LogWarning("DataSource {DataSourceId} no longer exists; failure not recorded", dataSource.Id);
                return ApplyFailure(dataSource, reason);
            }

            ApplyFailure(current, reason);
            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return current;
        }
        catch (Exception saveEx)
        {
            logger.LogError(saveEx, "Failed to record failure status for DataSource {DataSourceId}", dataSource.Id);
            return ApplyFailure(dataSource, reason);
        }
    }

    private static DataSource ApplyFailure(DataSource dataSource, string reason)
    {
        dataSource.Status = IngestionStatus.Failed;
        dataSource.ErrorMessage = reason;
        return dataSource;
    }
}

/// <summary>
/// Client-visible <see cref="DataSource.ErrorMessage"/> values. They name the
/// failed stage only; the underlying exception is logged, never returned.
/// </summary>
public static class IngestionFailures
{
    public const string UnsupportedSourceType = "Unsupported source type";
    public const string ExtractionFailed = "Text extraction failed";
    public const string NoExtractableText = "No extractable text";
    public const string ChunkingFailed = "Text chunking failed";
    public const string EmbeddingFailed = "Embedding generation failed";
    public const string PersistenceFailed = "Saving extracted content failed";
    public const string Cancelled = "Ingestion was cancelled";
}
