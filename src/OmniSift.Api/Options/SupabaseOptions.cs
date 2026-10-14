namespace OmniSift.Api.Options;

/// <summary>
/// Configuration for validating Supabase-issued JWTs via OIDC discovery.
/// When <see cref="Url"/> is set, the API validates ES256/RS256 access tokens
/// against Supabase's published JWKS (no symmetric secret). When it is empty,
/// the legacy in-house HS256 tokens (<see cref="JwtOptions"/>) are validated
/// instead — intended for local development only.
/// </summary>
public sealed class SupabaseOptions
{
    public const string Section = "Supabase";

    /// <summary>
    /// Base Supabase project URL, e.g. <c>https://abcd.supabase.co</c>.
    /// Empty disables Supabase validation and selects the legacy HS256 path.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Expected JWT audience. Supabase access tokens carry <c>aud="authenticated"</c>.
    /// </summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>
    /// True when Supabase validation is configured. False selects the
    /// development-only in-house HS256 identity provider (AuthController).
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);
}
