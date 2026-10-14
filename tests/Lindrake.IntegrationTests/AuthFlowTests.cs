// ============================================================
// Integration Tests — HS256 register / login flow
// Runs the in-house identity provider end to end: register a
// tenant owner, log in, and use (or forge) the issued token.
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lindrake.IntegrationTests.ProblemAssertions;

namespace Lindrake.IntegrationTests;

public sealed class AuthFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "correct-horse-battery";

    private readonly CustomWebApplicationFactory _factory;

    public AuthFlowTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_NewEmail_Returns200WithTenantAndToken()
    {
        var email = NewEmail();

        var response = await RegisterAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        auth.TenantId.Should().NotBeEmpty();
        auth.UserId.Should().NotBeEmpty();
        auth.Role.Should().Be("owner");
        auth.Token.Should().NotBeNullOrEmpty();
        auth.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        using var scope = _factory.CreateTenantScope(auth.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        (await db.Tenants.AnyAsync(t => t.Id == auth.TenantId)).Should().BeTrue();
        (await db.Users.SingleAsync()).Email.Should().Be(email);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409Problem()
    {
        var email = NewEmail();
        (await RegisterAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await RegisterAsync(email);

        await ShouldBeProblemAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_ValidCredentials_TokenAuthorizesProtectedEndpoint()
    {
        var email = NewEmail();
        var registered = await RegisterAndReadAsync(email);

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = Password,
            TenantId = registered.TenantId
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        auth.TenantId.Should().Be(registered.TenantId);
        auth.UserId.Should().Be(registered.UserId);

        var export = await ClientWithToken(auth.Token).GetAsync("/api/account/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        (await export.Content.ReadFromJsonAsync<TenantExportDto>())!.TenantId.Should().Be(registered.TenantId);
    }

    [Fact]
    public async Task Login_WrongTenantId_Returns401Problem()
    {
        var email = NewEmail();
        await RegisterAndReadAsync(email);

        var response = await LoginAsync(email, Password, Guid.NewGuid());

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_EmptyTenantId_Returns401Problem()
    {
        var email = NewEmail();
        await RegisterAndReadAsync(email);

        var response = await LoginAsync(email, Password, Guid.Empty);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401Problem()
    {
        var email = NewEmail();
        var registered = await RegisterAndReadAsync(email);

        var response = await LoginAsync(email, "wrong-password", registered.TenantId);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_TamperedTokenPayload_Returns401()
    {
        // Re-point the issued token at another tenant, keeping the signature.
        var registered = await RegisterAndReadAsync(NewEmail());
        var tampered = WithTenantClaim(registered.Token, Guid.NewGuid());

        var response = await ClientWithToken(tampered).GetAsync("/api/datasources");

        await ShouldBeInvalidTokenAsync(response);
    }

    [Fact]
    public async Task ProtectedEndpoint_TokenSignedWithOtherKey_Returns401()
    {
        var registered = await RegisterAndReadAsync(NewEmail());
        var forged = ResignedWith(registered.Token, "a-different-signing-secret-of-sufficient-length");

        var response = await ClientWithToken(forged).GetAsync("/api/datasources");

        await ShouldBeInvalidTokenAsync(response);
    }

    [Fact]
    public async Task ProtectedEndpoint_ExpiredToken_Returns401()
    {
        // Expired well outside the validator's default five-minute clock skew.
        var token = CustomWebApplicationFactory.CreateSignedToken(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("tenant_id", CustomWebApplicationFactory.TestTenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddHours(-1));

        var response = await ClientWithToken(token).GetAsync("/api/datasources");

        await ShouldBeInvalidTokenAsync(response);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("expired");
    }

    private static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Password = Password,
            TenantName = "Byrne Family Archive"
        });

    private async Task<AuthResponse> RegisterAndReadAsync(string email)
    {
        var response = await RegisterAsync(email);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password, Guid tenantId) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
            TenantId = tenantId
        });

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// A bearer challenge for a token that was presented but failed validation:
    /// the RFC 6750 header for clients that read it, a ProblemDetails body for the rest.
    /// </summary>
    private static async Task ShouldBeInvalidTokenAsync(HttpResponseMessage response)
    {
        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("error=\"invalid_token\"");
    }


    /// <summary>Rewrites the token's <c>tenant_id</c> claim without re-signing it.</summary>
    private static string WithTenantClaim(string token, Guid tenantId)
    {
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlDecode(parts[1]))!.AsObject();
        payload["tenant_id"] = tenantId.ToString();
        parts[1] = Base64UrlEncode(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        return string.Join('.', parts);
    }

    /// <summary>Keeps the token's header and payload but signs them with <paramref name="secret"/>.</summary>
    private static string ResignedWith(string token, string secret)
    {
        var parts = token.Split('.');
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
        return $"{parts[0]}.{parts[1]}.{Base64UrlEncode(signature)}";
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
