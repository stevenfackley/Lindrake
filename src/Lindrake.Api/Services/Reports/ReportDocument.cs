// ============================================================
// Lindrake.Api — Research report model
// The normalized input every report renderer consumes.
// ============================================================

using System.Globalization;
using Lindrake.Api.Options;
using Lindrake.Api.Services.Agent;
using Lindrake.Shared.DTOs;

namespace Lindrake.Api.Services.Reports;

/// <summary>A single conversation turn to export.</summary>
public sealed record ReportMessage
{
    /// <summary>"user" or "assistant", in any case; a turn with another role is not rendered.</summary>
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<SourceCitation> Citations { get; init; } = [];

    /// <summary>A query turn: <see cref="Role"/> is "user", ignoring case.</summary>
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    /// <summary>An answer turn: <see cref="Role"/> is "assistant", ignoring case.</summary>
    public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);
}

/// <summary>How every report renderer names and scores a <see cref="SourceCitation"/>.</summary>
public static class ReportCitations
{
    /// <summary>The citation's title, else its URL, else "(unknown)".</summary>
    public static string Label(SourceCitation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        return citation.Title ?? citation.Url ?? "(unknown)";
    }

    /// <summary>The relevance score to three decimals (culture-invariant), or null when there is none.</summary>
    public static string? Score(SourceCitation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        return citation.RelevanceScore?.ToString("F3", CultureInfo.InvariantCulture);
    }
}

/// <summary>A research report: title, generation time and the conversation.</summary>
public sealed record ReportDocument
{
    public string Title { get; init; } = ResearchProfiles.FamilyHistory.ReportTitle;

    public IReadOnlyList<ReportMessage> Messages { get; init; } = [];

    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary><see cref="GeneratedAt"/> as ISO-8601 UTC, e.g. 2026-06-21T12:00:00Z (culture-invariant).</summary>
    public string Timestamp => GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Download file name, e.g. report-20260621120000.pdf (culture-invariant).</summary>
    public string FileName(string extension) =>
        $"report-{GeneratedAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.{extension}";
}

/// <summary>Maps an API report request onto a <see cref="ReportDocument"/>.</summary>
public static class ReportRequestFactory
{
    /// <summary>
    /// Builds the document, stamped with <paramref name="timeProvider"/>'s current UTC
    /// time. A blank request title falls back to the research profile's title template.
    /// </summary>
    public static ReportDocument From(
        GenerateReportRequest request,
        TimeProvider timeProvider,
        ResearchProfileOptions profile)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(profile);

        var now = timeProvider.GetUtcNow();

        return new ReportDocument
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? profile.FormatReportTitle(now) : request.Title,
            GeneratedAt = now,
            Messages = [.. request.Messages.Select(m => new ReportMessage
            {
                Role = m.Role,
                Content = m.Content,
                Citations = [.. m.Citations]
            })]
        };
    }
}

/// <summary>Renders a <see cref="ReportDocument"/> into one output format.</summary>
public interface IReportRenderer<out TOutput>
{
    TOutput Render(ReportDocument document);
}
