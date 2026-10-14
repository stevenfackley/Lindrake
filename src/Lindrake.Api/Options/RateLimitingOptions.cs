// ============================================================
// Lindrake.Api — Rate limiting configuration options
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace Lindrake.Api.Options;

/// <summary>
/// Per-tenant token bucket applied to the data endpoints.
/// Bound from the "RateLimiting" configuration section.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    /// <summary>Bucket capacity (burst size) per tenant.</summary>
    [Range(1, int.MaxValue)]
    public int TokenLimit { get; set; } = 20;

    /// <summary>Tokens added back every <see cref="ReplenishmentPeriod"/>.</summary>
    [Range(1, int.MaxValue)]
    public int TokensPerPeriod { get; set; } = 10;

    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
