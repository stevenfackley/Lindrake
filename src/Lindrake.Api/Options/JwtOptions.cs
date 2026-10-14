namespace OmniSift.Api.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "OmniSift";
    public string Audience { get; set; } = "OmniSift";
    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Opt-in to run the in-house HS256 identity provider (register/login, self-signed
    /// tokens) outside Development/Testing when Supabase:Url is empty. Off by default:
    /// a deployment that merely forgets Supabase:Url refuses to start instead.
    /// Requires a unique <see cref="Secret"/> of at least 32 bytes.
    /// </summary>
    public bool AllowLocalIdentityProvider { get; set; }
}
