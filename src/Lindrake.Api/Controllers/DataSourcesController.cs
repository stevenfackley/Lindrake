// ============================================================
// OmniSift.Api — Data Sources Controller
// Manages file uploads, web ingestion, and data source CRUD
// ============================================================

using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Data.Projections;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Api.Services;
using OmniSift.Api.Services.Extraction;
using OmniSift.Api.Services.Ingestion;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.PerTenant)]
public sealed class DataSourcesController(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    IIngestionPipeline ingestionPipeline,
    ITextExtractorRegistry extractorRegistry,
    IngestionChannel ingestionChannel,
    IAuditLogger auditLogger,
    ILogger<DataSourcesController> logger) : ControllerBase
{
    /// <summary>
    /// Maximum upload file size (50 MB).
    /// </summary>
    private const long MaxFileSize = 50 * 1024 * 1024;

    /// <summary>Column widths of data_sources.file_name / source_type.</summary>
    private const int MaxFileNameLength = 512;
    private const int MaxSourceTypeLength = 50;

    /// <summary>
    /// Upload a file for asynchronous ingestion into the document pipeline.
    /// Accepts PDF, CSV/JSON (SMS), HTML, DOCX, and EML/MBOX (email) files.
    /// Returns 202 Accepted immediately; poll GET /api/datasources/{id} for status.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<IngestionResponse>> Upload(
        IFormFile file,
        [FromForm, MaxLength(MaxSourceTypeLength)] string? sourceType,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(detail: "No file provided or file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > MaxFileSize)
        {
            return Problem(
                detail: $"File size exceeds the maximum allowed size of {MaxFileSize / (1024 * 1024)} MB.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Longer would fail the insert with a DbUpdateException (500).
        if (file.FileName.Length > MaxFileNameLength)
        {
            return Problem(
                detail: $"File name exceeds the maximum length of {MaxFileNameLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Determine the source type from the explicit parameter or from the
        // MIME type / file extension — validated here, not in the background worker.
        string? resolvedSourceType;

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            if (!extractorRegistry.TryGet(sourceType, out var extractor))
                throw new UnsupportedSourceTypeException(sourceType, extractorRegistry.SupportedSourceTypes);

            resolvedSourceType = extractor.SourceType;
        }
        else if (!extractorRegistry.TryResolveSourceType(file.ContentType, file.FileName, out resolvedSourceType))
        {
            throw new UnsupportedFileTypeException(file.ContentType, file.FileName, extractorRegistry.SupportedSourceTypes);
        }

        var tenantId = tenantContext.TenantId;

        logger.LogInformation(
            "Upload request: file={FileName}, size={Size}, type={SourceType}, tenant={TenantId}",
            file.FileName, file.Length, resolvedSourceType, tenantId);

        // Buffer the file into memory before the HTTP request ends.
        // The background worker runs after the response is sent so
        // the IFormFile stream will be disposed by then.
        byte[] content;
        using (var ms = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            content = ms.ToArray();
        }

        // Create the DataSource record synchronously in this request
        // so the client has an ID to poll immediately.
        var dataSource = new DataSource
        {
            TenantId = tenantId,
            SourceType = resolvedSourceType,
            FileName = file.FileName,
            Status = IngestionStatus.Pending
        };

        dbContext.DataSources.Add(dataSource);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditLogger.LogAsync("upload", "data_source", dataSource.Id, cancellationToken).ConfigureAwait(false);

        // Enqueue for background processing. The worker carries tenantId
        // and binds its scope to that tenant (query filter + RLS).
        var workItem = new IngestionWorkItem(
            DataSourceId: dataSource.Id,
            TenantId: tenantId,
            SourceType: resolvedSourceType,
            FileName: file.FileName,
            Content: content);

        try
        {
            await ingestionChannel.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            logger.LogError("Ingestion channel is closed; cannot enqueue DataSource {DataSourceId}", dataSource.Id);
            return Problem(
                detail: "Ingestion service unavailable. Please try again shortly.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        logger.LogInformation(
            "DataSource {DataSourceId} enqueued for background ingestion (tenant={TenantId})",
            dataSource.Id, tenantId);

        return Accepted(new IngestionResponse
        {
            DataSourceId = dataSource.Id,
            Status = dataSource.Status.ToWireString(),
            Message = "File accepted. Poll GET /api/datasources/{id} for ingestion status."
        });
    }

    /// <summary>
    /// Ingest a web page by URL.
    /// </summary>
    [HttpPost("web")]
    public async Task<ActionResult<IngestionResponse>> IngestWeb(
        [FromBody] WebIngestionRequest request,
        [FromServices] IWebPageFetcher webPageFetcher,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Web ingestion request: url={Url}, tenant={TenantId}",
            request.Url, tenantContext.TenantId);

        // SSRF-guarded fetch. BlockedUrlException → 400; HttpRequestException
        // (upstream failure, oversized body) → 502 via GlobalExceptionHandler.
        var page = await webPageFetcher.FetchAsync(request.Url, cancellationToken).ConfigureAwait(false);

        // Committed before the pipeline runs so a pipeline failure is recorded on
        // this row (status "failed") instead of rolling it back.
        var dataSource = new DataSource
        {
            TenantId = tenantContext.TenantId,
            SourceType = SourceTypes.Web,
            OriginalUrl = request.Url,
            Status = IngestionStatus.Processing
        };
        dbContext.DataSources.Add(dataSource);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        using var stream = new MemoryStream(page, writable: false);
        dataSource = await ingestionPipeline.RunAsync(dataSource, stream, cancellationToken).ConfigureAwait(false);

        return Ok(new IngestionResponse
        {
            DataSourceId = dataSource.Id,
            Status = dataSource.Status.ToWireString(),
            Message = dataSource.Status == IngestionStatus.Completed
                ? "Web page ingested successfully."
                : $"Ingestion {dataSource.Status.ToWireString()}: {dataSource.ErrorMessage}"
        });
    }

    /// <summary>
    /// List the current tenant's data sources, newest first, one page at a time
    /// (<paramref name="take"/> defaults to <see cref="DataSourceProjections.DefaultTake"/>,
    /// at most <see cref="DataSourceProjections.MaxTake"/>).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DataSourceDto>>> List(
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, DataSourceProjections.MaxTake)] int take = DataSourceProjections.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var sources = await dbContext.DataSources
            .ToDtoPage(skip, take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Ok(sources);
    }

    /// <summary>
    /// Get a single data source by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DataSourceDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var source = await dbContext.DataSources
            .Where(ds => ds.Id == id)
            .Select(DataSourceProjections.ToDto)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (source is null)
            return Problem(detail: $"Data source '{id}' not found.", statusCode: StatusCodes.Status404NotFound);

        return Ok(source);
    }

    /// <summary>
    /// Delete a data source and all associated document chunks.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var source = await dbContext.DataSources
            .FirstOrDefaultAsync(ds => ds.Id == id, cancellationToken).ConfigureAwait(false);

        if (source is null)
            return Problem(detail: $"Data source '{id}' not found.", statusCode: StatusCodes.Status404NotFound);

        dbContext.DataSources.Remove(source);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditLogger.LogAsync("delete", "data_source", id, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Deleted DataSource {DataSourceId} for tenant {TenantId}",
            id, tenantContext.TenantId);

        return NoContent();
    }
}
