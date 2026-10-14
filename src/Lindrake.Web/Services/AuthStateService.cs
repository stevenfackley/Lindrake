using System.Security.Claims;
using Microsoft.JSInterop;

namespace Lindrake.Web.Services;

/// <summary>
/// The client's authentication state: the JWT persisted in localStorage, the
/// claims it carries and its expiry. A token past its <c>exp</c> claim counts as
/// signed out. Raises <see cref="OnAuthChanged"/> on sign-in, sign-out and when
/// the token expires, so routing and the nav menu update live.
/// <para>
/// Plays the role of Blazor's <c>AuthenticationStateProvider</c>, which this app
/// cannot use without adding the Microsoft.AspNetCore.Components.Authorization package.
/// </para>
/// Registered as a singleton: <see cref="AuthHandler"/> runs in the HTTP client
/// factory's own DI scope and must see the same instance as the components.
/// </summary>
public sealed class AuthStateService(IJSRuntime js, TimeProvider time)
{
    private const string TokenKey = "lindrake_token";
    private const string LastTenantKey = "lindrake_last_tenant";

    // A timer longer than this would overflow; it fires early and re-arms nothing,
    // which is harmless (IsAuthenticated reads the clock on every call).
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(1);

    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private string? _token;
    private JwtSession? _session;
    private ITimer? _expiryTimer;

    public event Action? OnAuthChanged;

    /// <summary>A token is stored and has not expired.</summary>
    public bool IsAuthenticated => _session is { } session && !session.IsExpired(time.GetUtcNow());

    /// <summary>The bearer token, or null when signed out or expired.</summary>
    public string? Token => IsAuthenticated ? _token : null;

    /// <summary>The token's claims (<c>sub</c>, <c>tenant_id</c>, <c>email</c>, …); anonymous when signed out.</summary>
    public ClaimsPrincipal User => IsAuthenticated ? _session!.User : Anonymous;

    /// <summary>When the current token expires; null when signed out.</summary>
    public DateTimeOffset? ExpiresAt => IsAuthenticated ? _session!.ExpiresAt : null;

    /// <summary>
    /// Called once on startup (before the app renders) to hydrate from localStorage.
    /// A stored token that is unreadable or expired is discarded.
    /// </summary>
    public async Task InitializeAsync()
    {
        var token = await js.InvokeAsync<string?>("localStorage.getItem", TokenKey).ConfigureAwait(false);
        if (token is null)
            return;

        var session = JwtSession.TryRead(token);
        if (session is null || session.IsExpired(time.GetUtcNow()))
        {
            await js.InvokeVoidAsync("localStorage.removeItem", TokenKey).ConfigureAwait(false);
            return;
        }

        SetSession(token, session);
    }

    /// <summary>
    /// Stores <paramref name="token"/> and remembers <paramref name="tenantId"/> for the
    /// next sign-in. Returns false (and stays signed out) when the token cannot be
    /// read or has already expired.
    /// </summary>
    public async Task<bool> SignInAsync(string token, Guid tenantId)
    {
        var session = JwtSession.TryRead(token);
        if (session is null || session.IsExpired(time.GetUtcNow()))
            return false;

        SetSession(token, session);
        await js.InvokeVoidAsync("localStorage.setItem", TokenKey, token).ConfigureAwait(false);
        await RememberTenantAsync(tenantId).ConfigureAwait(false);
        OnAuthChanged?.Invoke();
        return true;
    }

    /// <summary>Forgets the token. A no-op (no event) when none is stored.</summary>
    public async Task SignOutAsync()
    {
        if (_token is null)
            return;

        ClearSession();
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

    private void SetSession(string token, JwtSession session)
    {
        ClearSession();
        _token = token;
        _session = session;

        // Tell subscribers when the token lapses, so a page left open redirects
        // to the login page instead of failing its next request.
        var delay = session.ExpiresAt - time.GetUtcNow();
        delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxTimerDelay ? MaxTimerDelay : delay;
        _expiryTimer = time.CreateTimer(_ => OnAuthChanged?.Invoke(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private void ClearSession()
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;
        _token = null;
        _session = null;
    }
}
