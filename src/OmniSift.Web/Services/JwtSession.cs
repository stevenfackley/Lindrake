// ============================================================
// OmniSift.Web — JWT payload reader (no signature check)
// ============================================================

using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Json;

namespace OmniSift.Web.Services;

/// <summary>
/// The claims and expiry carried by a JWT, read client-side so the UI knows who
/// is signed in and until when. The signature is NOT verified — the API does
/// that on every request; this only decides what the UI shows.
/// </summary>
public sealed record JwtSession(ClaimsPrincipal User, DateTimeOffset ExpiresAt)
{
    /// <summary>Authentication type of <see cref="User"/>'s identity.</summary>
    public const string AuthenticationType = "jwt";

    /// <summary>True once <paramref name="now"/> has reached the <c>exp</c> claim.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>
    /// Reads the payload of <paramref name="token"/> (base64url JSON, the middle
    /// segment). Null when the token is malformed or has no numeric <c>exp</c> —
    /// the API rejects such tokens anyway.
    /// </summary>
    public static JwtSession? TryRead(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 3 || parts[1].Length == 0)
            return null;

        byte[] payload;
        try
        {
            payload = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ReadExpiry(root) is not { } expiresAt)
                return null;

            var claims = new List<Claim>();
            foreach (var property in root.EnumerateObject())
                AddClaims(claims, property.Name, property.Value);

            var identity = new ClaimsIdentity(claims, AuthenticationType, nameType: "email", roleType: "role");
            return new JwtSession(new ClaimsPrincipal(identity), expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // exp is NumericDate: seconds since the epoch, possibly fractional.
    private static DateTimeOffset? ReadExpiry(JsonElement root)
    {
        if (!root.TryGetProperty("exp", out var exp)
            || exp.ValueKind != JsonValueKind.Number
            || !exp.TryGetDouble(out var seconds)
            || double.IsNaN(seconds)
            || seconds < 0
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }

    private static void AddClaims(List<Claim> claims, string name, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                claims.Add(new Claim(name, value.GetString()!));
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                claims.Add(new Claim(name, value.GetRawText()));
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        AddClaims(claims, name, item);
                }

                break;
            case JsonValueKind.Object:
                // e.g. Supabase app_metadata — kept as raw JSON.
                claims.Add(new Claim(name, value.GetRawText(), "json"));
                break;
        }
    }
}
