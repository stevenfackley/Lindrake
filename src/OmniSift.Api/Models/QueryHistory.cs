// ============================================================
// OmniSift.Api — QueryHistory Entity
// Stores agent query/response pairs for audit and context
// ============================================================

namespace OmniSift.Api.Models;

/// <summary>
/// Records every agent query and response for audit trails
/// and potential conversation context retrieval.
/// </summary>
public sealed class QueryHistory : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the owning tenant. Required for RLS.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The user's original query text.
    /// </summary>
    public string QueryText { get; set; } = string.Empty;

    /// <summary>
    /// The agent's response text.
    /// </summary>
    public string? ResponseText { get; set; }

    /// <summary>
    /// List of plugin names invoked during query processing.
    /// </summary>
    public List<string> PluginsUsed { get; set; } = [];

    /// <summary>
    /// Source citations included in the response.
    /// </summary>
    public List<object> Sources { get; set; } = [];

    /// <summary>
    /// Processing duration in milliseconds.
    /// </summary>
    public int? DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
}
