// ============================================================
// Unit Tests — IngestionPipeline
// Asserts on what was actually persisted, read back through a
// fresh DbContext. EF InMemory has no transactions, so these
// prove the saved end state, not commit/rollback mechanics.
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Lindrake.Api.Options;
using Lindrake.Api.Services;
using Lindrake.Api.Services.Extraction;
using Lindrake.Api.Services.Ingestion;
using Lindrake.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pgvector;

namespace Lindrake.UnitTests.Services;

public sealed class IngestionPipelineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly string _databaseName = "pipeline-" + Guid.NewGuid();
    private readonly Mock<ITextExtractor> _extractor = new();
    private readonly Mock<IEmbeddingService> _embeddings = new();

    public IngestionPipelineTests()
    {
        _extractor.SetupGet(e => e.SourceType).Returns("pdf");
        _embeddings
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> texts, CancellationToken _) =>
                texts.Select(_ => new Vector(new float[384])).ToList());
    }

    [Fact]
    public async Task RunAsync_Success_PersistsChunksAndCompleted()
    {
        _extractor.Setup(ExtractCall()).ReturnsAsync("My SSN is 123-45-6789; see the quarterly report.");

        var (db, dataSource) = await SeedAsync();
        var result = await CreatePipeline(db).RunAsync(dataSource, Stream.Null);

        result.Status.Should().Be(IngestionStatus.Completed);

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Completed);
        saved.ErrorMessage.Should().BeNull();
        saved.HasPii.Should().BeTrue();
        (await verify.DocumentChunks.CountAsync(c => c.DataSourceId == dataSource.Id && c.TenantId == TenantId))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_ExtractorThrows_CommitsFailedWithSafeMessage()
    {
        _extractor.Setup(ExtractCall()).ThrowsAsync(new InvalidOperationException("secret path C:\\internal\\x.pdf"));

        var (db, dataSource) = await SeedAsync();
        var result = await CreatePipeline(db).RunAsync(dataSource, Stream.Null);

        result.Status.Should().Be(IngestionStatus.Failed);

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Failed);
        saved.ErrorMessage.Should().Be(IngestionFailures.ExtractionFailed);
        (await verify.DocumentChunks.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_EmptyText_CommitsFailed()
    {
        _extractor.Setup(ExtractCall()).ReturnsAsync("   ");

        var (db, dataSource) = await SeedAsync();
        await CreatePipeline(db).RunAsync(dataSource, Stream.Null);

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Failed);
        saved.ErrorMessage.Should().Be(IngestionFailures.NoExtractableText);
    }

    [Fact]
    public async Task RunAsync_EmbeddingFails_CommitsFailed()
    {
        _extractor.Setup(ExtractCall()).ReturnsAsync("some text worth embedding");
        _embeddings
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream 500"));

        var (db, dataSource) = await SeedAsync();
        await CreatePipeline(db).RunAsync(dataSource, Stream.Null);

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Failed);
        saved.ErrorMessage.Should().Be(IngestionFailures.EmbeddingFailed);
    }

    [Fact]
    public async Task RunAsync_ChunkSaveFails_DiscardsChunksAndCommitsFailed()
    {
        _extractor.Setup(ExtractCall()).ReturnsAsync("some text worth embedding");

        var (db, dataSource) = await SeedAsync(new FailOnChunkInsertInterceptor());
        var result = await CreatePipeline(db).RunAsync(dataSource, Stream.Null);

        result.Status.Should().Be(IngestionStatus.Failed);

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Failed);
        saved.ErrorMessage.Should().Be(IngestionFailures.PersistenceFailed);
        (await verify.DocumentChunks.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_Cancelled_CommitsFailedAndRethrows()
    {
        using var cts = new CancellationTokenSource();
        _extractor.Setup(ExtractCall()).Returns(async () =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            return "unreachable";
        });

        var (db, dataSource) = await SeedAsync();
        var act = () => CreatePipeline(db).RunAsync(dataSource, Stream.Null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Failed);
        saved.ErrorMessage.Should().Be(IngestionFailures.Cancelled);
    }

    [Fact]
    public async Task RunAsync_NoExtractorForSourceType_CommitsFailed()
    {
        var (db, dataSource) = await SeedAsync();
        dataSource.SourceType = "spreadsheet";

        await CreatePipeline(db).RunAsync(dataSource, Stream.Null);

        await using var verify = InMemoryDb.ForTenant(_databaseName, TenantId);
        var saved = await verify.DataSources.SingleAsync();
        saved.Status.Should().Be(IngestionStatus.Failed);
        saved.ErrorMessage.Should().Be(IngestionFailures.UnsupportedSourceType);
    }

    [Fact]
    public async Task RunAsync_DataSourceOfAnotherTenant_Throws()
    {
        var (db, dataSource) = await SeedAsync();
        var otherTenant = new TenantContext();
        otherTenant.Set(Guid.NewGuid());

        var act = () => CreatePipeline(db, otherTenant).RunAsync(dataSource, Stream.Null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static System.Linq.Expressions.Expression<Func<ITextExtractor, Task<string>>> ExtractCall() =>
        e => e.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>());

    private async Task<(LindrakeDbContext Db, DataSource DataSource)> SeedAsync(params IInterceptor[] interceptors)
    {
        var db = InMemoryDb.ForTenant(_databaseName, TenantId, interceptors);
        var dataSource = new DataSource
        {
            TenantId = TenantId,
            SourceType = "pdf",
            FileName = "report.pdf",
            Status = IngestionStatus.Processing
        };
        db.DataSources.Add(dataSource);
        await db.SaveChangesAsync();
        return (db, dataSource);
    }

    private IngestionPipeline CreatePipeline(LindrakeDbContext db, ITenantContext? tenantContext = null)
    {
        if (tenantContext is null)
        {
            var context = new TenantContext();
            context.Set(TenantId);
            tenantContext = context;
        }

        return new IngestionPipeline(
            db,
            tenantContext,
            new TextExtractorRegistry([_extractor.Object]),
            new TextChunker(new WordHeuristicTokenCounter(), Options.Create(new ChunkingOptions())),
            _embeddings.Object,
            NullLogger<IngestionPipeline>.Instance);
    }

    /// <summary>Simulates the chunk insert failing (e.g. a constraint violation).</summary>
    private sealed class FailOnChunkInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var insertingChunks = eventData.Context!.ChangeTracker
                .Entries<DocumentChunk>()
                .Any(e => e.State == EntityState.Added);

            return insertingChunks
                ? throw new DbUpdateException("simulated chunk insert failure")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
