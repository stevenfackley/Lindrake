// ============================================================
// OmniSift.Api — Hosting environment names
// ============================================================

namespace OmniSift.Api.Infrastructure;

/// <summary>
/// Environment names beyond the framework's Development/Staging/Production.
/// </summary>
public static class OmniSiftEnvironments
{
    /// <summary>
    /// Integration-test host (CustomWebApplicationFactory). InMemory database,
    /// no Serilog host logger, no relational migrations.
    /// </summary>
    public const string Testing = "Testing";

    /// <summary>True when running under the integration-test host.</summary>
    public static bool IsTesting(this IHostEnvironment environment) =>
        environment.IsEnvironment(Testing);
}
