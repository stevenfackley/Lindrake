// ============================================================
// Integration Tests — Authentication & tenant-from-claim
// Verifies bearer auth is required and that the tenant is derived
// from the validated JWT claim, never the X-Tenant-Id header.
// ============================================================

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OmniSift.Api.Data;
using OmniSift.Api.Models;
using OmniSift.Shared.DTOs;
using static OmniSift.IntegrationTests.ProblemAssertions;

namespace OmniSift.IntegrationTests;

public sealed class AuthenticationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthenticationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task DataEndpoint_WithoutToken_Returns401ProblemWithBearerChallenge()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/datasources");

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task DataEndpoint_WithValidToken_Returns200()
    {
        var client = _factory.CreateTenantClient();

        var response = await client.GetAsync("/api/datasources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DataEndpoint_AuthenticatedWithoutTenantClaim_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildTokenWithoutTenant());

        var response = await client.GetAsync("/api/datasources");

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousEndpoint_WithTokenLackingTenantClaim_IsServed()
    {
        // [AllowAnonymous] endpoints skip tenant resolution entirely.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildTokenWithoutTenant());

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantIsolation_IsDerivedFromClaim_OnlySeesOwnTenant()
    {
        var ownerTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await SeedDataSourceAsync(ownerTenant, "owned.pdf");

        var ownerClient = _factory.CreateTenantClient(ownerTenant);
        var ownerSources = await ownerClient.GetFromJsonAsync<List<DataSourceDto>>("/api/datasources");
        ownerSources.Should().ContainSingle(s => s.FileName == "owned.pdf");

        var otherClient = _factory.CreateTenantClient(otherTenant);
        var otherSources = await otherClient.GetFromJsonAsync<List<DataSourceDto>>("/api/datasources");
        otherSources.Should().NotContain(s => s.FileName == "owned.pdf");
    }

    [Fact]
    public async Task SpoofedTenantHeader_IsIgnored_TenantComesFromClaim()
    {
        var realTenant = Guid.NewGuid();
        var spoofedTenant = Guid.NewGuid();
        await SeedDataSourceAsync(realTenant, "real.pdf");

        // Authenticated as realTenant, but try to select a different tenant via header.
        var client = _factory.CreateTenantClient(realTenant);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", spoofedTenant.ToString());

        var sources = await client.GetFromJsonAsync<List<DataSourceDto>>("/api/datasources");

        // The header is ignored — the request stays scoped to the token's tenant.
        sources.Should().ContainSingle(s => s.FileName == "real.pdf");
    }

    private async Task SeedDataSourceAsync(Guid tenantId, string fileName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();
        db.DataSources.Add(new DataSource
        {
            TenantId = tenantId,
            SourceType = "pdf",
            FileName = fileName,
            Status = IngestionStatus.Completed
        });
        await db.SaveChangesAsync();
    }

    private static string BuildTokenWithoutTenant()
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: CustomWebApplicationFactory.TestJwtIssuer,
            audience: CustomWebApplicationFactory.TestJwtAudience,
            claims: [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "authenticated")],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
