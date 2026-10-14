// ============================================================
// Unit Tests — the Blazor client's HTTP stack over a fake handler
// ============================================================

using System.Buffers.Text;
using System.Text;
using Lindrake.Web.Services;

namespace Lindrake.UnitTests.TestSupport;

/// <summary>
/// Wires <see cref="LindrakeApiClient"/> → <see cref="AuthHandler"/> → a
/// <see cref="FakeHttpMessageHandler"/>, with <see cref="AuthStateService"/> over an
/// in-memory localStorage — the same chain Program.cs registers.
/// </summary>
internal sealed class WebClientHarness : IDisposable
{
    private readonly HttpClient _http;

    public WebClientHarness(FakeHttpMessageHandler handler, TimeProvider? time = null)
    {
        Handler = handler;
        Time = time ?? TimeProvider.System;
        Auth = new AuthStateService(Js, Time);
        _http = new HttpClient(new AuthHandler(Auth) { InnerHandler = handler })
        {
            BaseAddress = new Uri("http://api.test/")
        };
        Api = new LindrakeApiClient(_http, Auth);
    }

    public FakeJsRuntime Js { get; } = new();
    public TimeProvider Time { get; }
    public AuthStateService Auth { get; }
    public FakeHttpMessageHandler Handler { get; }
    public LindrakeApiClient Api { get; }

    public static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>Signs in with a token that expires in an hour.</summary>
    public Task<string> SignInAsync()
    {
        var token = TestJwt.Create(Time.GetUtcNow().AddHours(1), TenantId);
        return SignInAsync(token);
    }

    public async Task<string> SignInAsync(string token)
    {
        await Auth.SignInAsync(token, TenantId);
        return token;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Unsigned JWTs with the claims the API issues (the client never verifies signatures).</summary>
internal static class TestJwt
{
    public static string Create(DateTimeOffset expiresAt, Guid tenantId, string email = "user@example.test") =>
        CreateRaw($$"""{"sub":"{{Guid.NewGuid()}}","tenant_id":"{{tenantId}}","email":"{{email}}","role":"owner","exp":{{expiresAt.ToUnixTimeSeconds()}}}""");

    public static string CreateRaw(string payloadJson) =>
        $"{Encode("""{"alg":"HS256","typ":"JWT"}""")}.{Encode(payloadJson)}.c2lnbmF0dXJl";

    private static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
}
