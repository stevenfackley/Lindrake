// ============================================================
// OmniSift.Api — Auth mode startup validation
// ============================================================

using System.Text;
using Microsoft.Extensions.Options;
using OmniSift.Api.Infrastructure;

namespace OmniSift.Api.Options;

/// <summary>
/// Refuses to start the development-only HS256 identity provider outside
/// Development/Testing, and refuses a weak signing secret when it is active.
/// The HS256 provider is selected implicitly whenever Supabase:Url is empty,
/// so without this guard a deployment that forgets Supabase:Url silently
/// issues and accepts self-signed tokens.
/// </summary>
internal sealed class JwtOptionsValidator(
    IOptions<SupabaseOptions> supabaseOptions,
    IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    /// <summary>HMAC-SHA256 needs a key of at least 256 bits.</summary>
    public const int MinSecretBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (supabaseOptions.Value.IsConfigured)
            return ValidateOptionsResult.Success;

        if (!environment.IsDevelopment() && !environment.IsTesting())
        {
            return ValidateOptionsResult.Fail(
                $"Supabase:Url is not set, which selects the development-only HS256 identity provider, " +
                $"but the environment is '{environment.EnvironmentName}'. " +
                "Set Supabase:Url (env Supabase__Url) to validate Supabase-issued tokens.");
        }

        if (Encoding.UTF8.GetByteCount(options.Secret) < MinSecretBytes)
        {
            return ValidateOptionsResult.Fail(
                $"Jwt:Secret must be at least {MinSecretBytes} bytes while the HS256 identity provider " +
                "is active (Supabase:Url empty).");
        }

        return ValidateOptionsResult.Success;
    }
}
