// ============================================================
// OmniSift.Shared — Report DTOs
// Request/Response contracts for the research report endpoint
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace OmniSift.Shared.DTOs;

/// <summary>
/// A single turn in the conversation to export as a report.
/// </summary>
public sealed record ReportTurn
{
    /// <summary>"user" or "assistant"</summary>
    [Required]
    public string Role { get; init; } = string.Empty;

    [Required]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Citations attached to this turn (typically only assistant turns have these).
    /// </summary>
    public List<SourceCitation> Citations { get; init; } = [];
}

/// <summary>
/// Request body for POST /api/agent/report.
/// </summary>
public sealed record GenerateReportRequest
{
    /// <summary>
    /// Report title (defaults to "Research Report" server-side if omitted).
    /// </summary>
    [MaxLength(256)]
    public string? Title { get; init; }

    /// <summary>
    /// The conversation turns to include in the report.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<ReportTurn> Messages { get; init; } = [];
}

/// <summary>
/// Response body for POST /api/agent/report.
/// </summary>
public sealed record GenerateReportResponse
{
    /// <summary>
    /// Markdown-formatted research report.
    /// </summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>
    /// Report title as used in the document.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// ISO 8601 timestamp embedded in the report.
    /// </summary>
    public string GeneratedAt { get; init; } = string.Empty;
}
