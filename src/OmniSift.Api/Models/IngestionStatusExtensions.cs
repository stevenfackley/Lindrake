// ============================================================
// OmniSift.Api — IngestionStatus wire/database strings
// ============================================================

namespace OmniSift.Api.Models;

/// <summary>
/// The one mapping between <see cref="IngestionStatus"/> and its lowercase string
/// form, used both on the wire (DTO <c>status</c> fields) and in the
/// <c>data_sources.status</c> column.
/// </summary>
public static class IngestionStatusExtensions
{
    /// <summary><c>"pending"</c>, <c>"processing"</c>, <c>"completed"</c> or <c>"failed"</c>.</summary>
    public static string ToWireString(this IngestionStatus status) => status switch
    {
        IngestionStatus.Pending => "pending",
        IngestionStatus.Processing => "processing",
        IngestionStatus.Completed => "completed",
        IngestionStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ingestion status.")
    };

    /// <summary>Parses a wire string (case-insensitive).</summary>
    public static bool TryParseWireString(string? value, out IngestionStatus status)
    {
        foreach (var candidate in Enum.GetValues<IngestionStatus>())
        {
            if (string.Equals(value, candidate.ToWireString(), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>Parses a wire string (case-insensitive).</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not an ingestion status.</exception>
    public static IngestionStatus ParseWireString(string value) =>
        TryParseWireString(value, out var status)
            ? status
            : throw new FormatException($"'{value}' is not an ingestion status.");
}
