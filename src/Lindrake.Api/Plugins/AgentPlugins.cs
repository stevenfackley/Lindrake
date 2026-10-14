// ============================================================
// Lindrake.Api — Kernel plugin names
// ============================================================

namespace Lindrake.Api.Plugins;

/// <summary>
/// Names under which the agent's plugins are imported into the Kernel. These are
/// the values a research profile lists in <c>ResearchProfile:EnabledPlugins</c>
/// and the names reported in <c>pluginsUsed</c>.
/// </summary>
public static class AgentPlugins
{
    public const string VectorSearch = "VectorSearch";
    public const string WebScraper = "WebScraper";
    public const string WaybackMachine = "WaybackMachine";

    /// <summary>Every plugin the agent can load, in import order.</summary>
    public static IReadOnlyList<string> All { get; } = [VectorSearch, WebScraper, WaybackMachine];
}
