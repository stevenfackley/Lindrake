// ============================================================
// OmniSift.Web — MarkdownService
// Renders markdown to safe HTML using Markdig.
// Raw inline HTML is DISABLED to prevent XSS from model output.
// ============================================================

using Markdig;

namespace OmniSift.Web.Services;

/// <summary>
/// Converts markdown text to sanitised HTML.
/// Registered as a singleton — Markdig pipelines are thread-safe.
/// </summary>
public sealed class MarkdownService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()   // tables, task lists, footnotes, etc.
        .DisableHtml()             // strip raw HTML blocks and inline HTML
        .Build();

    /// <summary>
    /// Renders <paramref name="markdown"/> to an HTML string.
    /// Raw HTML in the input is neutralised; the output is safe to inject as
    /// a <c>MarkupString</c> in Blazor.
    /// </summary>
    public string Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, _pipeline);
    }
}
