// ============================================================
// Integration Tests — Per-tenant rate limiting
// Verifies the token bucket is partitioned by the tenant_id claim,
// so one tenant exhausting its budget does not throttle another.
// ============================================================

using System.Net;
using FluentAssertions;
using static Lindrake.IntegrationTests.ProblemAssertions;

namespace Lindrake.IntegrationTests;

public sealed class RateLimitingTests : IDisposable
{
    // Own factory (not the shared class fixture) so the bucket state is isolated.
    private readonly CustomWebApplicationFactory _factory = new()
    {
        ExtraSettings = new Dictionary<string, string?>
        {
            ["RateLimiting:TokenLimit"] = "2",
            ["RateLimiting:TokensPerPeriod"] = "1",
            ["RateLimiting:ReplenishmentPeriod"] = "01:00:00",
        }
    };

    [Fact]
    public async Task PerTenantBucket_ExhaustedByOneTenant_DoesNotThrottleAnother()
    {
        var tenantA = _factory.CreateTenantClient(Guid.NewGuid());
        var tenantB = _factory.CreateTenantClient(Guid.NewGuid());

        (await tenantA.GetAsync("/api/datasources")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await tenantA.GetAsync("/api/datasources")).StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldBeProblemAsync(await tenantA.GetAsync("/api/datasources"), HttpStatusCode.TooManyRequests);

        (await tenantB.GetAsync("/api/datasources")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public void Dispose() => _factory.Dispose();
}
