// ============================================================
// OmniSift.Api — Built-in research profiles
// A profile is the agent's persona: system prompt, plugin set and
// report titling. Deployments pick one through the ResearchProfile
// configuration section; this file holds the built-in defaults.
// ============================================================

using OmniSift.Api.Plugins;

namespace OmniSift.Api.Services.Agent;

public static class ResearchProfiles
{
    /// <summary>
    /// The original OmniSift profile (personal and family-history research), and
    /// the default whenever <c>ResearchProfile</c> is not configured.
    /// </summary>
    public static class FamilyHistory
    {
        public const string Name = "FamilyHistory";

        public const string ReportTitle = "Research Report";

        public const string SystemPrompt = """
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

        public static IReadOnlyList<string> Plugins => AgentPlugins.All;
    }
}
