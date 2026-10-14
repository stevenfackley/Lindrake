// ============================================================
// OmniSift.Web — Bearer token message handler
// ============================================================

using System.Net.Http.Headers;

namespace OmniSift.Web.Services;

/// <summary>
/// Attaches <c>Authorization: Bearer &lt;jwt&gt;</c> to every API request while the
/// user is signed in. The tenant travels inside the token (<c>tenant_id</c> claim);
/// no tenant header is sent.
/// </summary>
public sealed class AuthHandler(AuthStateService auth) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers.Authorization is null && auth.Token is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
