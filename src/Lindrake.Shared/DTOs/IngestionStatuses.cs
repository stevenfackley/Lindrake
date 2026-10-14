// ============================================================
// Lindrake.Shared — Ingestion status wire strings
// ============================================================

namespace Lindrake.Shared.DTOs;

/// <summary>
/// The values of <see cref="DataSourceDto.Status"/> and <see cref="IngestionResponse.Status"/>
/// (and of the <c>data_sources.status</c> column). An upload moves
/// <see cref="Pending"/> → <see cref="Processing"/> → <see cref="Completed"/> or <see cref="Failed"/>.
/// </summary>
public static class IngestionStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";

    /// <summary>True for <see cref="Completed"/> and <see cref="Failed"/>: the status will not change again.</summary>
    public static bool IsTerminal(string? status) =>
        string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);
}
