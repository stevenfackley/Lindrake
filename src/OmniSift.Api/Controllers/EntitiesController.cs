// ============================================================
// OmniSift.Api — Entities Controller
// Extracts named entities and relationship graph from tenant documents
// ============================================================

using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.Api.Services;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.PerTenant)]
public sealed class EntitiesController(
    IEntityExtractionService extractor,
    ITenantContext tenantContext,
    ILogger<EntitiesController> logger) : ControllerBase
{
    /// <summary>
    /// Extracts a named-entity knowledge graph from the tenant's document chunks.
    /// Requires a configured LLM (Anthropic:ApiKey); without one the service throws
    /// LlmNotConfiguredException, which GlobalExceptionHandler maps to 503.
    /// </summary>
    [HttpPost("extract")]
    public async Task<ActionResult<EntityGraphResponse>> ExtractAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        logger.LogInformation(
            "Entity extraction started for tenant {TenantId}.",
            tenantContext.TenantId);

        var graph = await extractor.ExtractAsync(tenantContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        sw.Stop();

        logger.LogInformation(
            "Entity extraction completed for tenant {TenantId} in {DurationMs}ms — " +
            "{NodeCount} nodes, {EdgeCount} edges, {TimelineCount} timeline entries.",
            tenantContext.TenantId,
            sw.ElapsedMilliseconds,
            graph.Nodes.Count,
            graph.Edges.Count,
            graph.Timeline.Count);

        return Ok(graph);
    }
}
