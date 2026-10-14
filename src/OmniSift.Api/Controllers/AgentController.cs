// ============================================================
// OmniSift.Api — Agent Controller
// Handles research queries via Semantic Kernel agent
// ============================================================

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OmniSift.Api.Data;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.Api.Models;
using OmniSift.Api.Services;
using OmniSift.Shared;
using OmniSift.Shared.DTOs;

namespace OmniSift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting(RateLimitPolicies.PerTenant)]
public sealed class AgentController(
    Kernel kernel,
    OmniSiftDbContext dbContext,
    ITenantContext tenantContext,
    ICitationAccumulator citationAccumulator,
    IAuditLogger auditLogger,
    ILogger<AgentController> logger) : ControllerBase
{
    /// <summary>
    /// System prompt for the research agent.
    /// </summary>
    private const string SystemPrompt = """
        You are OmniSift, an AI research assistant. You help users find and analyze information
        from their uploaded documents and the web.

        Guidelines:
        - When answering questions, ALWAYS search the user's documents first using SearchDocuments.
        - If the documents don't contain sufficient information, supplement with web search using SearchWeb.
        - For questions about historical web pages, use GetArchivedPage to check the Wayback Machine.
        - Always cite your sources clearly. For document results, mention the file name and relevance.
        - For web results, include the URL.
        - Be thorough but concise. Present findings in a structured manner.
        - If you cannot find relevant information, say so honestly rather than speculating.
        - When presenting multiple pieces of information, organize them logically.
        """;

    /// <summary>
    /// Submit a research query to the AI agent.
    /// The agent will search documents, the web, and archives as needed.
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult<AgentQueryResponse>> Query(
        [FromBody] AgentQueryRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        logger.LogInformation(
            "Agent query from tenant {TenantId}: {QueryPreview}",
            tenantContext.TenantId,
            request.Query.Length > 100 ? request.Query[..100] + "..." : request.Query);

        var chatService = kernel.GetChatCompletionOrThrow();

        // Build chat history
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(SystemPrompt);

        // Add conversation history if provided
        if (request.ConversationHistory is not null)
        {
            foreach (var msg in request.ConversationHistory)
            {
                switch (msg.Role.ToLowerInvariant())
                {
                    case "user":
                        chatHistory.AddUserMessage(msg.Content);
                        break;
                    case "assistant":
                        chatHistory.AddAssistantMessage(msg.Content);
                        break;
                }
            }
        }

        // Add the current query
        chatHistory.AddUserMessage(request.Query);

        // Configure auto function calling
        var executionSettings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Execute with Semantic Kernel
        var result = await chatService.GetChatMessageContentAsync(
            chatHistory,
            executionSettings,
            kernel,
            cancellationToken).ConfigureAwait(false);

        sw.Stop();

        // Extract plugins used from function call results in chat history
        var pluginsUsed = chatHistory
            .Where(m => m.Role == AuthorRole.Tool)
            .Select(m => m.Metadata?.GetValueOrDefault("ChatCompletionsFunctionToolCall.Name")?.ToString())
            .Where(name => name is not null)
            .Distinct()
            .Cast<string>()
            .ToList();

        var responseText = result.Content ?? "I was unable to generate a response.";

        // Save to query history
        var queryHistory = new QueryHistory
        {
            TenantId = tenantContext.TenantId,
            QueryText = request.Query,
            ResponseText = responseText,
            PluginsUsed = pluginsUsed,
            DurationMs = (int)sw.ElapsedMilliseconds
        };

        dbContext.QueryHistories.Add(queryHistory);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditLogger.LogAsync("agent_query", "query_history", queryHistory.Id, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Agent query completed in {DurationMs}ms, plugins={Plugins}",
            sw.ElapsedMilliseconds, string.Join(", ", pluginsUsed));

        var sources = citationAccumulator.GetCitations();

        return Ok(new AgentQueryResponse
        {
            Response = responseText,
            PluginsUsed = pluginsUsed,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Sources = [.. sources]
        });
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
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Research Report" : request.Title;

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
        var title = string.IsNullOrWhiteSpace(request.Title) ? "Research Report" : request.Title;

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

    /// <summary>
    /// Submit a research query to the AI agent and receive a Server-Sent Events stream.
    /// Emits delta events for each token, then a final event with metadata and citations.
    /// </summary>
    [HttpPost("query/stream")]
    public async Task QueryStream(
        [FromBody] AgentQueryRequest request,
        CancellationToken cancellationToken)
    {
        // Resolve before the SSE headers are set so a missing LLM still gets a
        // plain 503 ProblemDetails response.
        var chatService = kernel.GetChatCompletionOrThrow();

        var sw = Stopwatch.StartNew();

        logger.LogInformation(
            "Agent stream query from tenant {TenantId}: {QueryPreview}",
            tenantContext.TenantId,
            request.Query.Length > 100 ? request.Query[..100] + "..." : request.Query);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(SystemPrompt);

        if (request.ConversationHistory is not null)
        {
            foreach (var msg in request.ConversationHistory)
            {
                switch (msg.Role.ToLowerInvariant())
                {
                    case "user":
                        chatHistory.AddUserMessage(msg.Content);
                        break;
                    case "assistant":
                        chatHistory.AddAssistantMessage(msg.Content);
                        break;
                }
            }
        }

        chatHistory.AddUserMessage(request.Query);

        var executionSettings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var fullText = new StringBuilder();

        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
            chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false))
        {
            var text = chunk.Content;
            if (string.IsNullOrEmpty(text))
                continue;

            fullText.Append(text);

            var delta = new AgentStreamDeltaEvent { Type = "delta", Content = text };
            var deltaJson = JsonSerializer.Serialize(delta, OmniSiftJsonContext.Default.AgentStreamDeltaEvent);
            await Response.WriteAsync($"data: {deltaJson}\n\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        sw.Stop();

        var pluginsUsed = chatHistory
            .Where(m => m.Role == AuthorRole.Tool)
            .Select(m => m.Metadata?.GetValueOrDefault("ChatCompletionsFunctionToolCall.Name")?.ToString())
            .Where(name => name is not null)
            .Distinct()
            .Cast<string>()
            .ToList();

        var responseText = fullText.ToString();
        if (string.IsNullOrEmpty(responseText))
            responseText = "I was unable to generate a response.";

        var sources = citationAccumulator.GetCitations();

        var finalEvent = new AgentStreamFinalEvent
        {
            Type = "final",
            PluginsUsed = pluginsUsed,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Sources = [.. sources]
        };
        var finalJson = JsonSerializer.Serialize(finalEvent, OmniSiftJsonContext.Default.AgentStreamFinalEvent);
        await Response.WriteAsync($"data: {finalJson}\n\n", cancellationToken).ConfigureAwait(false);
        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Save to query history
        var queryHistory = new QueryHistory
        {
            TenantId = tenantContext.TenantId,
            QueryText = request.Query,
            ResponseText = responseText,
            PluginsUsed = pluginsUsed,
            DurationMs = (int)sw.ElapsedMilliseconds
        };

        dbContext.QueryHistories.Add(queryHistory);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditLogger.LogAsync("agent_query_stream", "query_history", queryHistory.Id, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Agent stream query completed in {DurationMs}ms, plugins={Plugins}",
            sw.ElapsedMilliseconds, string.Join(", ", pluginsUsed));
    }
}
