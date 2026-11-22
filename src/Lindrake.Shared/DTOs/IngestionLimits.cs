// ============================================================
// Lindrake.Shared — Ingestion limits
// ============================================================

namespace Lindrake.Shared.DTOs;

/// <summary>
/// Limits the API enforces on ingestion, which the Web client checks before it
/// sends anything.
/// </summary>
public static class IngestionLimits
{
    /// <summary>Largest accepted upload, in bytes (50 MB); a file of exactly this size is accepted.</summary>
    public const long MaxUploadBytes = 50L * 1024 * 1024;
}
