// ============================================================
// Lindrake.Api — Auth mode startup validation
// ============================================================

using System.Text;
using Lindrake.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Lindrake.Api.Options;

/// <summary>
/// Guards the in-house HS256 identity provider, which is selected implicitly
/// whenever Supabase:Url is empty. In Development/Testing it only needs a 256-bit
/// secret. Anywhere else it must be switched on deliberately with
/// <see cref="JwtOptions.AllowLocalIdentityProvider"/> and a secret that is at least
/// 32 bytes and not the public development default — so a deployment that forgets
/// Supabase:Url refuses to start instead of silently issuing self-signed tokens.
/// </summary>
internal sealed class JwtOptionsValidator(
    IOptions<SupabaseOptions> supabaseOptions,
    IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    /// <summary>HMAC-SHA256 needs a key of at least 256 bits.</summary>
    public const int MinSecretBytes = 32;

    /// <summary>
    /// The Jwt:Secret in appsettings.Development.json. It is in the repository, so
    /// anyone can sign tokens with it: never acceptable outside Development/Testing.
    /// </summary>
    internal const string DevelopmentDefaultSecret = "dev-only-hs256-secret-not-for-production-use";

    private const string OptInFlag = "Jwt:AllowLocalIdentityProvider (env JWT__ALLOWLOCALIDENTITYPROVIDER)";

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (supabaseOptions.Value.IsConfigured)
            return ValidateOptionsResult.Success;

        var developmentOrTesting = environment.IsDevelopment() || environment.IsTesting();

        if (!developmentOrTesting && !options.AllowLocalIdentityProvider)
        {
            return ValidateOptionsResult.Fail(
                "Supabase:Url is not set, which selects the in-house HS256 identity provider, " +
                $"but the environment is '{environment.EnvironmentName}'. " +
                "Set Supabase:Url (env Supabase__Url) to validate Supabase-issued tokens, " +
                $"or set {OptInFlag}=true to run the HS256 provider deliberately.");
        }

        if (Encoding.UTF8.GetByteCount(options.Secret) < MinSecretBytes)
        {
            return ValidateOptionsResult.Fail(
                $"Jwt:Secret must be at least {MinSecretBytes} bytes while the HS256 identity provider " +
                (developmentOrTesting
                    ? "is active (Supabase:Url empty)."
                    : $"is enabled by {OptInFlag}."));
        }

        if (!developmentOrTesting && string.Equals(options.Secret, DevelopmentDefaultSecret, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Jwt:Secret is the development default from appsettings.Development.json, which is public. " +
                $"Set a unique secret of at least {MinSecretBytes} bytes when {OptInFlag} is enabled.");
        }

        return ValidateOptionsResult.Success;
    }
}
