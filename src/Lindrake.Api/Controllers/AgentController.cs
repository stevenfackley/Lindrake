// ============================================================
// Lindrake.Api — Agent Controller
// Thin HTTP adapter over IResearchAgentService
// ============================================================

using Lindrake.Api.Infrastructure;
using Lindrake.Api.Options;
using Lindrake.Api.Services.Agent;
using Lindrake.Api.Services.Reports;
using Lindrake.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Lindrake.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.PerTenant)]
public sealed class AgentController(
    IResearchAgentService agent,
    AgentSseWriter sseWriter,
    IOptions<ResearchProfileOptions> profileOptions,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>
    /// Submit a research query to the AI agent.
    /// The agent will search documents, the web, and archives as needed.
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<AgentQueryResponse>> Query(
        [FromBody] AgentQueryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await agent.RunAsync(request, cancellationToken).ConfigureAwait(false);
        return Ok(result.ToResponse());
    }

    /// <summary>
    /// Submit a research query to the AI agent and receive a Server-Sent Events stream.
    /// Emits delta events for each token, then a final event with metadata and citations.
    /// </summary>
    [HttpPost("query/stream")]
    public async Task QueryStream(
        [FromBody] AgentQueryRequest request,
        CancellationToken cancellationToken)
    {
        // Throws before any SSE header is written, so a missing LLM is still a plain 503.
        var events = agent.StreamAsync(request, cancellationToken);
        await sseWriter.WriteAsync(Response, events, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Generate a formatted Markdown research report from a conversation
    /// with its associated citations.
    /// </summary>
    [HttpPost("report")]
    public ActionResult<GenerateReportResponse> GenerateReport(
        [FromBody] GenerateReportRequest request,
        [FromServices] IReportRenderer<string> markdownRenderer)
    {
        var document = ReportRequestFactory.From(request, timeProvider, profileOptions.Value);

        return Ok(new GenerateReportResponse
        {
            Markdown = markdownRenderer.Render(document),
            Title = document.Title,
            GeneratedAt = document.Timestamp
        });
    }

    /// <summary>
    /// Generate a PDF research report from a conversation with its associated citations.
    /// </summary>
    [HttpPost("report/pdf")]
    public ActionResult GenerateReportPdf(
        [FromBody] GenerateReportRequest request,
        [FromServices] IReportRenderer<byte[]> pdfRenderer)
    {
        var document = ReportRequestFactory.From(request, timeProvider, profileOptions.Value);
        return File(pdfRenderer.Render(document), PdfReportRenderer.ContentType, document.FileName("pdf"));
    }
}
