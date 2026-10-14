// ============================================================
// Lindrake.Web — Which pages need a signed-in user
// ============================================================

using Microsoft.AspNetCore.Authorization;

namespace Lindrake.Web.Services;

/// <summary>
/// Routing rules for <c>AuthRouteView</c>: a page carrying <see cref="AuthorizeAttribute"/>
/// (every page under Pages/, via Pages/_Imports.razor) needs a signed-in user unless it
/// also carries <see cref="AllowAnonymousAttribute"/>.
/// </summary>
public static class PageAccess
{
    /// <summary>True when <paramref name="pageType"/> may only be shown to a signed-in user.</summary>
    public static bool RequiresAuthentication(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        return Attribute.IsDefined(pageType, typeof(AuthorizeAttribute), inherit: true)
            && !Attribute.IsDefined(pageType, typeof(AllowAnonymousAttribute), inherit: true);
    }

    /// <summary>
    /// The base-relative URL to return to after signing in: <paramref name="returnUrl"/>
    /// when it is a local path, otherwise the home page (""). Rejects absolute and
    /// protocol-relative URLs (open redirect) and the login page itself (loop).
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return string.Empty;

        var url = returnUrl.Trim();
        var isLocal = !url.StartsWith('/')
            && !url.StartsWith('\\')
            && !url.Contains(':', StringComparison.Ordinal)
            && Uri.TryCreate(url, UriKind.Relative, out _);

        if (!isLocal || url.Equals("login", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("login?", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return url;
    }
}
