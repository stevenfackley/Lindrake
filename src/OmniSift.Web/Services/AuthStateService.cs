using Microsoft.JSInterop;

namespace OmniSift.Web.Services;

/// <summary>
/// Persists the JWT to localStorage and provides reactive auth state.
/// Notifies subscribers when auth changes so NavMenu can update live.
/// Registered as a singleton: <see cref="AuthHandler"/> runs in the HTTP client
/// factory's own DI scope and must see the same instance as the components.
/// </summary>
public sealed class AuthStateService(IJSRuntime js)
{
    private const string TokenKey = "omnisift_token";
    private const string LastTenantKey = "omnisift_last_tenant";

    private string? _token;

    public event Action? OnAuthChanged;

    public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
    public string? Token => _token;

    /// <summary>
    /// Called once on startup (before the app renders) to hydrate from localStorage.
    /// </summary>
    public async Task InitializeAsync()
    {
        _token = await js.InvokeAsync<string?>("localStorage.getItem", TokenKey).ConfigureAwait(false);
    }

    /// <summary>Stores the token and remembers <paramref name="tenantId"/> for the next sign-in.</summary>
    public async Task SignInAsync(string token, Guid tenantId)
    {
        _token = token;
        await js.InvokeVoidAsync("localStorage.setItem", TokenKey, token).ConfigureAwait(false);
        await RememberTenantAsync(tenantId).ConfigureAwait(false);
        OnAuthChanged?.Invoke();
    }

    public async Task SignOutAsync()
    {
        _token = null;
        await js.InvokeVoidAsync("localStorage.removeItem", TokenKey).ConfigureAwait(false);
        OnAuthChanged?.Invoke();
    }

    /// <summary>The tenant of the last successful sign-in or registration, if any.</summary>
    public async Task<Guid?> GetLastTenantIdAsync()
    {
        var value = await js.InvokeAsync<string?>("localStorage.getItem", LastTenantKey).ConfigureAwait(false);
        return Guid.TryParse(value, out var tenantId) && tenantId != Guid.Empty ? tenantId : null;
    }

    /// <summary>Remembers <paramref name="tenantId"/> so the login form can prefill it.</summary>
    public async Task RememberTenantAsync(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            return;

        await js.InvokeVoidAsync("localStorage.setItem", LastTenantKey, tenantId.ToString()).ConfigureAwait(false);
    }
}
