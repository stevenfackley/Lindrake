// ============================================================
// Lindrake.Api — DocumentChunk Entity
// Stores text chunks with vector embeddings for semantic search
// ============================================================

using Pgvector;

namespace Lindrake.Api.Models;

/// <summary>
/// An individual text chunk extracted from a data source,
/// with a vector embedding for semantic similarity search.
/// </summary>
public sealed class DocumentChunk : ITenantOwned, ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the owning tenant. Required for RLS.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Foreign key to the parent data source.
    /// </summary>
    public Guid DataSourceId { get; set; }

    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Sequential index of this chunk within the data source (0-based).
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Approximate token count of this chunk.
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// Vector embedding. Dimension is fixed by the pgvector column (384 for the
    /// local bge-small ONNX model) and MUST match the active embedding provider.
    /// Null until embedding is generated.
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>
    /// Flexible metadata stored as JSONB (e.g., page number, timestamp).
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public DataSource DataSource { get; set; } = null!;
}
