// ============================================================
// OmniSift.Api — Ingestion Background Service
// Drains the IngestionChannel and runs the ingestion pipeline
// outside the HTTP request lifecycle.
//
// TENANT / RLS SAFETY:
//   The work item carries the tenantId captured at enqueue time.
//   For each item this service:
//     1. Creates a fresh DI scope (own DbContext connection).
//     2. Binds the scope's ITenantContext to the item's tenant.
//     3. Sets app.current_tenant on the connection
//        — identical to what TenantMiddleware does per HTTP request.
//     4. Only then loads the DataSource and runs the pipeline.
//   This ensures RLS is active for every DB write made by the worker.
// ============================================================

using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Api.Services.Ingestion;

namespace OmniSift.Api.Services;

/// <summary>
/// Long-running hosted service that drains the <see cref="IngestionChannel"/>
/// and runs <see cref="IIngestionPipeline"/> for each item in a dedicated DI scope.
/// </summary>
public sealed class IngestionBackgroundService(
    IngestionChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingestion background service started.");

        await foreach (var item in channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await ProcessItemAsync(item, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An exception escaping ExecuteAsync stops the host; one bad item
                // (e.g. the database briefly unreachable) must not do that.
                logger.LogError(ex,
                    "Background ingestion failed for DataSource {DataSourceId}",
                    item.DataSourceId);
            }
        }

        logger.LogInformation("Ingestion background service stopped.");
    }

    private async Task ProcessItemAsync(IngestionWorkItem item, CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Background ingestion starting: DataSource={DataSourceId}, Tenant={TenantId}, Type={SourceType}",
            item.DataSourceId, item.TenantId, item.SourceType);

        // Each item gets its own scope so its DbContext gets its own connection.
#pragma warning disable CA2007 // await using DisposeAsync — no SyncContext in hosted service
        await using var scope = scopeFactory.CreateAsyncScope();
#pragma warning restore CA2007
        var services = scope.ServiceProvider;

        // Background equivalent of TenantMiddleware: bind the scope to the item's
        // tenant, then scope the connection to it (RLS) BEFORE any EF query.
        services.GetRequiredService<ITenantContext>().Set(item.TenantId);
        var dbContext = services.GetRequiredService<OmniSiftDbContext>();
        await dbContext.SetTenantSessionAsync(item.TenantId, stoppingToken).ConfigureAwait(false);

        var dataSource = await dbContext.DataSources
            .FirstOrDefaultAsync(
                ds => ds.Id == item.DataSourceId && ds.TenantId == item.TenantId,
                stoppingToken)
            .ConfigureAwait(false);

        if (dataSource is null)
        {
            logger.LogError(
                "DataSource {DataSourceId} not found during background ingestion. Skipping.",
                item.DataSourceId);
            return;
        }

        dataSource.Status = IngestionStatus.Processing;
        dataSource.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

        using var content = new MemoryStream(item.Content, writable: false);
        var result = await services.GetRequiredService<IIngestionPipeline>()
            .RunAsync(dataSource, content, stoppingToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Background ingestion finished for DataSource {DataSourceId}: {Status}",
            item.DataSourceId, result.Status);
    }
}
