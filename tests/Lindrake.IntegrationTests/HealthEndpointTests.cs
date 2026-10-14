// ============================================================
// Integration Tests — Health Endpoint
// Verifies health check returns OK without tenant header
// ============================================================

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using static Lindrake.IntegrationTests.ProblemAssertions;

namespace Lindrake.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void HealthChecks_IncludeDatabase()
    {
        var options = _factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        options.Registrations.Should().Contain(r => r.Name == "LindrakeDbContext");
    }

    [Fact]
    public async Task LegacyApiHealthController_IsGone()
    {
        var response = await _client.GetAsync("/api/health");

        // Unmatched routes get the same ProblemDetails body as every other error.
        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_WithoutTenantHeader_Returns200()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_WithInvalidBearerToken_Returns200()
    {
        // A probe configured with a stale or garbage token must not read as down.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ReturnsHealthyPayload()
    {
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Contain("Healthy");
    }
}
