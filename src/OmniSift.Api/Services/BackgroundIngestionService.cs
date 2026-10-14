// ============================================================
// OmniSift.Api — Background Ingestion Service
// Runs the extract→chunk→embed pipeline for a DataSource
// whose row already exists and whose RLS context has already
// been set on the DbContext connection by the caller.
// ============================================================

using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Models;

namespace OmniSift.Api.Services;

/// <summary>
/// Pipeline runner used by <see cref="IngestionBackgroundService"/>.
/// Operates on a pre-loaded DataSource and a pre-opened connection
/// (with the PG tenant session variable already set by the caller).
/// </summary>
public interface IBackgroundIngestionService
{
    /// <summary>
    /// Runs the extract→chunk→embed steps and persists results into
    /// <paramref name="dataSource"/>. Updates Status to Completed or Failed.
    /// </summary>
    Task RunPipelineAsync(
        DataSource dataSource,
        Stream content,
        string sourceType,
        string? fileName,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BackgroundIngestionService(
    OmniSiftDbContext dbContext,
    [FromKeyedServices("pdf")] ITextExtractor pdfExtractor,
    [FromKeyedServices("sms")] ITextExtractor smsExtractor,
    [FromKeyedServices("web")] ITextExtractor webExtractor,
    [FromKeyedServices("docx")] ITextExtractor docxExtractor,
    [FromKeyedServices("email")] ITextExtractor emailExtractor,
    ITextChunker chunker,
    IEmbeddingService embeddingService,
    ILogger<BackgroundIngestionService> logger) : IBackgroundIngestionService
{
    private const int EmbeddingBatchSize = 20;

    /// <inheritdoc />
    public async Task RunPipelineAsync(
        DataSource dataSource,
        Stream content,
        string sourceType,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var extractor = sourceType switch
        {
            "pdf" => pdfExtractor,
            "sms" => smsExtractor,
            "web" => webExtractor,
            "docx" => docxExtractor,
            "email" => emailExtractor,
            _ => throw new UnsupportedSourceTypeException(sourceType, SourceTypes.All)
        };

        var tenantId = dataSource.TenantId;

        // EnableRetryOnFailure on the DbContext requires wrapping user transactions
        // inside the execution strategy — same pattern as DocumentIngestionService.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
#pragma warning disable CA2007 // await using DisposeAsync — no SyncContext in hosted service
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007

            try
            {
                var rawText = await extractor.ExtractTextAsync(content, fileName, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    dataSource.Status = IngestionStatus.Failed;
                    dataSource.ErrorMessage = "No text could be extracted from the source.";
                    dataSource.UpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                logger.LogDebug("Extracted {Length} chars from DataSource {Id}", rawText.Length, dataSource.Id);

                // PII scan (non-blocking — flag only, never reject)
                var piiMatches = PiiScanner.Scan(rawText);
                dataSource.HasPii = piiMatches.Count > 0;
                dataSource.PiiFlags = piiMatches.Count > 0
                    ? [.. piiMatches.Select(m => m.Type.ToString())]
                    : null;

                var chunks = chunker.ChunkText(rawText);
                logger.LogDebug("Created {ChunkCount} chunks from DataSource {Id}", chunks.Count, dataSource.Id);

                var allEmbeddings = new List<Pgvector.Vector>();

                for (var i = 0; i < chunks.Count; i += EmbeddingBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = chunks.Skip(i).Take(EmbeddingBatchSize).ToList();
                    var embeddings = await embeddingService
                        .GenerateEmbeddingsAsync(batch.Select(c => c.Content), cancellationToken)
                        .ConfigureAwait(false);
                    allEmbeddings.AddRange(embeddings);
                }

                var documentChunks = chunks.Select((chunk, idx) => new DocumentChunk
                {
                    TenantId = tenantId,
                    DataSourceId = dataSource.Id,
                    Content = chunk.Content,
                    ChunkIndex = chunk.Index,
                    TokenCount = chunk.TokenCount,
                    Embedding = allEmbeddings[idx],
                    Metadata = new Dictionary<string, object>
                    {
                        ["source_type"] = sourceType,
                        ["file_name"] = fileName ?? string.Empty
                    }
                }).ToList();

                dbContext.DocumentChunks.AddRange(documentChunks);

                dataSource.Status = IngestionStatus.Completed;
                dataSource.UpdatedAt = DateTime.UtcNow;
                dataSource.Metadata = new Dictionary<string, object>
                {
                    ["chunk_count"] = documentChunks.Count,
                    ["total_tokens"] = documentChunks.Sum(c => c.TokenCount),
                    ["text_length"] = rawText.Length
                };

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Background pipeline completed for DataSource {Id}: {Chunks} chunks",
                    dataSource.Id, documentChunks.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background pipeline failed for DataSource {Id}", dataSource.Id);

                dataSource.Status = IngestionStatus.Failed;
                dataSource.ErrorMessage = ex.Message;
                dataSource.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx,
                        "Failed to persist failure status for DataSource {Id}", dataSource.Id);
                }

                throw;
            }
        }).ConfigureAwait(false);
    }
}
