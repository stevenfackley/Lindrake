// ============================================================
// OmniSift.Api — Data Sources Controller
// Manages file uploads, web ingestion, and data source CRUD
// ============================================================

using System.Collections.Frozen;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Data;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Api.Services;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.PerTenant)]
public sealed class DataSourcesController(
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    IDocumentIngestionService ingestionService,
    IngestionChannel ingestionChannel,
    IAuditLogger auditLogger,
    ILogger<DataSourcesController> logger) : ControllerBase
{
    /// <summary>
    /// Maximum upload file size (50 MB).
    /// </summary>
    private const long MaxFileSize = 50 * 1024 * 1024;

    /// <summary>
    /// Allowed MIME types for file uploads mapped to their ingestion source-type key.
    /// FrozenDictionary provides optimised read-only lookups for this static mapping.
    /// </summary>
    private static readonly FrozenDictionary<string, string> AllowedMimeTypes =
        new Dictionary<string, string>
        {
            ["application/pdf"] = "pdf",
            ["text/csv"] = "sms",
            ["application/json"] = "sms",
            ["text/html"] = "web",
            // Word documents
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = "docx",
            ["application/msword"] = "docx",
            // Email formats (RFC-2822 / MBOX)
            ["message/rfc822"] = "email",
            ["application/mbox"] = "email"
        }.ToFrozenDictionary();

    /// <summary>
    /// Upload a file for asynchronous ingestion into the document pipeline.
    /// Accepts PDF, CSV/JSON (SMS), HTML, DOCX, and EML/MBOX (email) files.
    /// Returns 202 Accepted immediately; poll GET /api/datasources/{id} for status.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<IngestionResponse>> Upload(
        IFormFile file,
        [FromForm] string? sourceType,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided or file is empty." });
        }

        if (file.Length > MaxFileSize)
        {
            return BadRequest(new { error = $"File size exceeds the maximum allowed size of {MaxFileSize / (1024 * 1024)} MB." });
        }

        // Determine source type from MIME type or explicit parameter
        var resolvedSourceType = sourceType?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(resolvedSourceType))
        {
            var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;

            if (!AllowedMimeTypes.TryGetValue(contentType, out resolvedSourceType))
            {
                return BadRequest(new
                {
                    error = $"Unsupported file type: '{contentType}'. Allowed: {string.Join(", ", AllowedMimeTypes.Keys)}"
                });
            }
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
        // and sets the PG session variable itself to preserve RLS.
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
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Ingestion service unavailable. Please try again shortly." });
        }

        logger.LogInformation(
            "DataSource {DataSourceId} enqueued for background ingestion (tenant={TenantId})",
            dataSource.Id, tenantId);

        return Accepted(new IngestionResponse
        {
            DataSourceId = dataSource.Id,
            Status = dataSource.Status.ToString().ToLowerInvariant(),
            Message = "File accepted. Poll GET /api/datasources/{id} for ingestion status."
        });
    }

    /// <summary>
    /// Ingest a web page by URL.
    /// </summary>
    [HttpPost("web")]
    public async Task<ActionResult<IngestionResponse>> IngestWeb(
        [FromBody] WebIngestionRequest request,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        logger.LogInformation(
            "Web ingestion request: url={Url}, tenant={TenantId}",
            request.Url, tenantContext.TenantId);

        // Fetch the web page — HttpRequestException propagates to GlobalExceptionHandler (502)
        var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(request.Url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dataSource = await ingestionService.IngestAsync(
            stream, "web", null, request.Url, cancellationToken).ConfigureAwait(false);

        return Ok(new IngestionResponse
        {
            DataSourceId = dataSource.Id,
            Status = dataSource.Status.ToString().ToLowerInvariant(),
            Message = dataSource.Status == IngestionStatus.Completed
                ? "Web page ingested successfully."
                : $"Ingestion {dataSource.Status.ToString().ToLowerInvariant()}: {dataSource.ErrorMessage}"
        });
    }

    /// <summary>
    /// List all data sources for the current tenant.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DataSourceDto>>> List(CancellationToken cancellationToken)
    {
        var entities = await dbContext.DataSources
            .Where(ds => ds.TenantId == tenantContext.TenantId)
            .OrderByDescending(ds => ds.CreatedAt)
            .Include(ds => ds.Chunks)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var sources = entities.Select(ds => new DataSourceDto
        {
            Id = ds.Id,
            SourceType = ds.SourceType,
            FileName = ds.FileName,
            OriginalUrl = ds.OriginalUrl,
            Status = ds.Status.ToString().ToLowerInvariant(),
            ErrorMessage = ds.ErrorMessage,
            Metadata = ds.Metadata,
            CreatedAt = ds.CreatedAt,
            UpdatedAt = ds.UpdatedAt,
            ChunkCount = ds.Chunks.Count
        }).ToList();

        return Ok(sources);
    }

    /// <summary>
    /// Get a single data source by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DataSourceDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var ds = await dbContext.DataSources
            .Include(ds => ds.Chunks)
            .FirstOrDefaultAsync(
                ds => ds.TenantId == tenantContext.TenantId && ds.Id == id,
                cancellationToken).ConfigureAwait(false);

        if (ds is null)
            return NotFound(new { error = $"Data source '{id}' not found." });

        var source = new DataSourceDto
        {
            Id = ds.Id,
            SourceType = ds.SourceType,
            FileName = ds.FileName,
            OriginalUrl = ds.OriginalUrl,
            Status = ds.Status.ToString().ToLowerInvariant(),
            ErrorMessage = ds.ErrorMessage,
            Metadata = ds.Metadata,
            CreatedAt = ds.CreatedAt,
            UpdatedAt = ds.UpdatedAt,
            ChunkCount = ds.Chunks.Count
        };

        return Ok(source);
    }

    /// <summary>
    /// Delete a data source and all associated document chunks.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var source = await dbContext.DataSources
            .FirstOrDefaultAsync(ds => ds.TenantId == tenantContext.TenantId && ds.Id == id, cancellationToken).ConfigureAwait(false);

        if (source is null)
            return NotFound(new { error = $"Data source '{id}' not found." });

        dbContext.DataSources.Remove(source);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditLogger.LogAsync("delete", "data_source", id, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Deleted DataSource {DataSourceId} for tenant {TenantId}",
            id, tenantContext.TenantId);

        return NoContent();
    }
}
