// ============================================================
// Lindrake.Api — Research agent configuration options
// ============================================================

using System.ComponentModel.DataAnnotations;
using Lindrake.Shared.DTOs;

namespace Lindrake.Api.Options;

/// <summary>
/// Server-side limits for the research agent.
/// Bound from the "Agent" configuration section.
/// </summary>
public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>
    /// Most recent conversation-history messages forwarded to the LLM; older ones
    /// are dropped. The server enforces this itself rather than trusting the
    /// client's own trimming. Default 20 (ten user/assistant exchanges).
    /// </summary>
    [Range(0, AgentQueryRequest.MaxConversationTurns)]
    public int MaxHistoryMessages { get; set; } = 20;
}
