// ============================================================
// OmniSift.Api — Document Ingestion Service
// Orchestrates the full ingestion pipeline:
// Upload → Validate → Extract Text → Chunk → Embed → Store
// ============================================================

using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;

namespace OmniSift.Api.Services;

/// <summary>
/// Orchestrates the data ingestion pipeline for all source types.
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// Ingests a file stream through the full pipeline.
    /// </summary>
    Task<DataSource> IngestAsync(
        Stream stream,
        string sourceType,
        string? fileName = null,
        string? originalUrl = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation that coordinates text extraction, chunking,
/// embedding generation, and database persistence.
/// Uses keyed DI to resolve the correct ITextExtractor per source type.
/// </summary>
public sealed class DocumentIngestionService(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    [FromKeyedServices("pdf")] ITextExtractor pdfExtractor,
    [FromKeyedServices("sms")] ITextExtractor smsExtractor,
    [FromKeyedServices("web")] ITextExtractor webExtractor,
    [FromKeyedServices("docx")] ITextExtractor docxExtractor,
    [FromKeyedServices("email")] ITextExtractor emailExtractor,
    ITextChunker chunker,
    IEmbeddingService embeddingService,
    ILogger<DocumentIngestionService> logger) : IDocumentIngestionService
{
    /// <summary>
    /// Allowed source types.
    /// </summary>
    private static readonly HashSet<string> AllowedSourceTypes = ["pdf", "sms", "web", "docx", "email"];

    /// <summary>
    /// Batch size for embedding generation.
    /// </summary>
    private const int EmbeddingBatchSize = 20;

    /// <inheritdoc />
    public async Task<DataSource> IngestAsync(
        Stream stream,
        string sourceType,
        string? fileName = null,
        string? originalUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);

        sourceType = sourceType.ToLowerInvariant();

        if (!AllowedSourceTypes.Contains(sourceType))
        {
            throw new ArgumentException(
                $"Unsupported source type: '{sourceType}'. Allowed: {string.Join(", ", AllowedSourceTypes)}",
                nameof(sourceType));
        }

        // Resolve the correct extractor for this source type via keyed DI
        var extractor = sourceType switch
        {
            "pdf" => pdfExtractor,
            "sms" => smsExtractor,
            "web" => webExtractor,
            "docx" => docxExtractor,
            "email" => emailExtractor,
            _ => throw new InvalidOperationException($"No text extractor registered for source type: {sourceType}")
        };

        var tenantId = tenantContext.TenantId;

        // EnableRetryOnFailure configures a retrying execution strategy, which forbids a
        // user-initiated transaction unless it runs INSIDE the strategy — so a transient
        // retry replays the whole unit rather than resuming a half-applied transaction.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Use a transaction to ensure atomicity: either all chunks + data source
            // are persisted, or the whole operation rolls back.
#pragma warning disable CA2007 // await using DisposeAsync — block-form restructure not worth it for ASP.NET Core (no SyncContext)
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2007

            // Create the data source record
            var dataSource = new DataSource
            {
                TenantId = tenantId,
                SourceType = sourceType,
                FileName = fileName,
                OriginalUrl = originalUrl,
                Status = IngestionStatus.Processing
            };

            dbContext.DataSources.Add(dataSource);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Starting ingestion for DataSource {DataSourceId}, type={SourceType}, tenant={TenantId}",
                dataSource.Id, sourceType, tenantId);

            try
            {
                // Step 1: Extract text
                var rawText = await extractor.ExtractTextAsync(stream, fileName, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    dataSource.Status = IngestionStatus.Failed;
                    dataSource.ErrorMessage = "No text could be extracted from the source.";
                    dataSource.UpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return dataSource;
                }

                logger.LogDebug("Extracted {Length} chars from source {DataSourceId}", rawText.Length, dataSource.Id);

                // Step 1b: PII scan (non-blocking — flag only, never reject)
                var piiMatches = PiiScanner.Scan(rawText);
                dataSource.HasPii = piiMatches.Count > 0;
                dataSource.PiiFlags = piiMatches.Count > 0
                    ? [.. piiMatches.Select(m => m.Type.ToString())]
                    : null;

                // Step 2: Chunk text
                var chunks = chunker.ChunkText(rawText);
                logger.LogDebug("Created {ChunkCount} chunks from source {DataSourceId}", chunks.Count, dataSource.Id);

                // Step 3: Generate embeddings in batches
                var allEmbeddings = new List<Pgvector.Vector>();

                for (var i = 0; i < chunks.Count; i += EmbeddingBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = chunks.Skip(i).Take(EmbeddingBatchSize).ToList();
                    var batchTexts = batch.Select(c => c.Content);
                    var embeddings = await embeddingService.GenerateEmbeddingsAsync(batchTexts, cancellationToken).ConfigureAwait(false);
                    allEmbeddings.AddRange(embeddings);

                    logger.LogDebug(
                        "Generated embeddings batch {Batch}/{Total} for source {DataSourceId}",
                        Math.Min(i + EmbeddingBatchSize, chunks.Count), chunks.Count, dataSource.Id);
                }

                // Step 4: Create document chunk entities and persist
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
                    "Ingestion completed for DataSource {DataSourceId}: {ChunkCount} chunks, {TokenCount} total tokens",
                    dataSource.Id, documentChunks.Count, documentChunks.Sum(c => c.TokenCount));

                return dataSource;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ingestion failed for DataSource {DataSourceId}", dataSource.Id);

                dataSource.Status = IngestionStatus.Failed;
                dataSource.ErrorMessage = ex.Message;
                dataSource.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx, "Failed to save error status for DataSource {DataSourceId}", dataSource.Id);
                }

                throw;
            }
        }).ConfigureAwait(false);
    }
}
