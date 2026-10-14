// ============================================================
// Lindrake.Web — API base address resolution
// ============================================================

namespace Lindrake.Web.Services;

/// <summary>
/// Turns the <c>ApiBaseUrl</c> setting (wwwroot/appsettings.json, rendered from
/// <c>API_BASE_URL</c> when the container starts) into the HttpClient base address.
/// </summary>
public static class ApiBaseAddress
{
    /// <summary>
    /// <paramref name="configured"/> when set (an absolute URL such as
    /// <c>http://localhost:5000</c>); otherwise the origin the app was served from,
    /// so <c>api/...</c> requests go to the same host behind a reverse proxy.
    /// The result always ends with <c>/</c> so relative paths append to it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="configured"/> is set but is not an absolute http(s) URL.
    /// </exception>
    public static Uri Resolve(string? configured, string hostBaseAddress)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return new Uri(new Uri(hostBaseAddress, UriKind.Absolute), "/");

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"ApiBaseUrl '{configured}' is not an absolute http(s) URL. " +
                "Leave it empty to call the API on the app's own origin.");
        }

        return uri.AbsolutePath.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}
