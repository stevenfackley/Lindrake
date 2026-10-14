// ============================================================
// OmniSift.Api — Agent Controller
// Thin HTTP adapter over IResearchAgentService
// ============================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Options;
using OmniSift.Api.Services;
using OmniSift.Api.Services.Agent;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.PerTenant)]
public sealed class AgentController(
    IResearchAgentService agent,
    AgentSseWriter sseWriter,
    IOptions<ResearchProfileOptions> profileOptions) : ControllerBase
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
        [FromBody] GenerateReportRequest request)
    {
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var title = string.IsNullOrWhiteSpace(request.Title) ? profileOptions.Value.FormatReportTitle(now) : request.Title;

        var reportRequest = new ReportRequest
        {
            Title = title,
            Timestamp = timestamp,
            Messages = [.. request.Messages.Select(m => new ReportMessage
            {
                Role = m.Role,
                Content = m.Content,
                Citations = [.. m.Citations]
            })]
        };

        var markdown = ResearchReportBuilder.Build(reportRequest, now);

        return Ok(new GenerateReportResponse
        {
            Markdown = markdown,
            Title = title,
            GeneratedAt = timestamp
        });
    }

    /// <summary>
    /// Generate a PDF research report from a conversation with its associated citations.
    /// </summary>
    [HttpPost("report/pdf")]
    public ActionResult GenerateReportPdf([FromBody] GenerateReportRequest request)
    {
        var now = DateTime.UtcNow;
        var timestamp = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var title = string.IsNullOrWhiteSpace(request.Title) ? profileOptions.Value.FormatReportTitle(now) : request.Title;

        var reportRequest = new ReportRequest
        {
            Title = title,
            Timestamp = timestamp,
            Messages = [.. request.Messages.Select(m => new ReportMessage
            {
                Role = m.Role,
                Content = m.Content,
                Citations = [.. m.Citations]
            })]
        };

        var pdfBytes = PdfReportBuilder.Build(reportRequest, now);
        var fileName = $"report-{now:yyyyMMddHHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }
}
