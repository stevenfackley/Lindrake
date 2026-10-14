// ============================================================
// Lindrake.Api — Local (HS256) identity provider endpoint gate
// ============================================================

using Lindrake.Api.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Lindrake.Api.Infrastructure;

/// <summary>
/// Removes <see cref="AuthController"/> (in-house register/login) from the
/// application model when Supabase validates tokens, so /api/auth/* does not
/// exist (404) instead of minting HS256 tokens nobody accepts.
/// </summary>
internal sealed class LocalIdentityProviderConvention(bool enabled) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        if (enabled)
            return;

        var localIdp = application.Controllers
            .Where(c => c.ControllerType.AsType() == typeof(AuthController))
            .ToList();

        foreach (var controller in localIdp)
            application.Controllers.Remove(controller);
    }
}
