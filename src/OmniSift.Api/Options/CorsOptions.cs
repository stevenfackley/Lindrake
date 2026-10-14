// ============================================================
// OmniSift.Api — CORS Configuration Options
// ============================================================

namespace OmniSift.Api.Options;

/// <summary>
/// Strongly-typed options for CORS policy configuration.
/// Bound from the "Cors" configuration section.
/// </summary>
public sealed class CorsOptions
{
    public const string Section = "Cors";

    /// <summary>
    /// Allowed origins for the default CORS policy. Empty by default: no
    /// cross-origin access unless configured. The local-dev origins live in
    /// appsettings.Development.json — an initializer here would be kept by the
    /// configuration binder, which APPENDS bound array items to it, so they
    /// would be allowed in every environment.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
