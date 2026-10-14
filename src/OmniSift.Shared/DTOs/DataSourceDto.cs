// ============================================================
// OmniSift.Shared — Data Source DTOs
// Shared contracts between API and Frontend
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace OmniSift.Shared.DTOs;

/// <summary>
/// Represents a data source record returned from the API.
/// </summary>
public sealed record DataSourceDto
{
    public Guid Id { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? OriginalUrl { get; init; }
    public string Status { get; init; } = "pending";
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int ChunkCount { get; init; }
    public bool HasPii { get; init; }
    public List<string>? PiiFlags { get; init; }
}

/// <summary>
/// Request DTO for uploading a web URL for ingestion.
/// </summary>
public sealed record WebIngestionRequest
{
    [Required]
    [Url]
    [MaxLength(2048)] // data_sources.original_url
    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// Response DTO after initiating an upload/ingestion.
/// </summary>
public sealed record IngestionResponse
{
    public Guid DataSourceId { get; init; }
    public string Status { get; init; } = "pending";
    public string Message { get; init; } = string.Empty;
}
