// ============================================================
// Integration Tests — Auth mode
// The in-house HS256 register/login endpoints exist only while
// Supabase validation is not configured.
// ============================================================

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lindrake.Shared.DTOs;

namespace Lindrake.IntegrationTests;

public sealed class AuthModeTests
{
    private static readonly LoginRequest UnknownUser = new()
    {
        Email = "nobody@example.com",
        Password = "wrong-password",
        TenantId = Guid.NewGuid()
    };

    [Fact]
    public async Task Login_WhenSupabaseConfigured_Returns404()
    {
        using var factory = new CustomWebApplicationFactory
        {
            ExtraSettings = new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://example-project.supabase.co",
            }
        };

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", UnknownUser);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_WhenHs256Active_IsServed()
    {
        using var factory = new CustomWebApplicationFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", UnknownUser);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
