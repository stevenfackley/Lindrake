// ============================================================
// Integration Tests — Health Endpoint
// Verifies health check returns OK without tenant header
// ============================================================

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace OmniSift.IntegrationTests;

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

        options.Registrations.Should().Contain(r => r.Name == "OmniSiftDbContext");
    }

    [Fact]
    public async Task LegacyApiHealthController_IsGone()
    {
        var response = await _client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_WithoutTenantHeader_Returns200()
    {
        var response = await _client.GetAsync("/health");

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
