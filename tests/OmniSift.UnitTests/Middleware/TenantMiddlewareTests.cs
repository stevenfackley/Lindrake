// ============================================================
// Unit Tests — TenantMiddleware
// Verifies tenant is derived from the authenticated claim, not a header
// ============================================================

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;
using OmniSift.UnitTests.TestSupport;

namespace OmniSift.UnitTests.Middleware;

public sealed class TenantMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_HealthCheckPath_SkipsTenantResolution()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/health");

        // Health path skips before the DbContext is used.
        await middleware.InvokeAsync(context, null!, new TenantContext());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SwaggerPath_SkipsTenantResolution()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/swagger/index.html");

        await middleware.InvokeAsync(context, null!, new TenantContext());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedRequest_CallsNextWithoutTenantContext()
    {
        // [Authorize] is responsible for the 401 on protected endpoints; the
        // middleware must not itself reject — it just continues without a tenant.
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/datasources");
        var tenantContext = new TenantContext();

        await middleware.InvokeAsync(context, null!, tenantContext);

        nextCalled.Should().BeTrue();
        var tenant = () => tenantContext.TenantId;
        tenant.Should().Throw<TenantContextMissingException>();
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithoutTenantClaim_Returns403()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/datasources");
        context.User = Authenticated(new Claim("sub", Guid.NewGuid().ToString()));

        await middleware.InvokeAsync(context, null!, new TenantContext());

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithTenantClaim_BindsTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/datasources");
        context.User = Authenticated(new Claim("tenant_id", tenantId.ToString()));
        var tenantContext = new TenantContext();

        // InMemory is non-relational, so the RLS set_config step is skipped; the
        // Postgres path is exercised by the integration tests.
        await using var db = InMemoryDb.Create("tenant-middleware-" + Guid.NewGuid());
        await middleware.InvokeAsync(context, db, tenantContext);

        nextCalled.Should().BeTrue();
        tenantContext.TenantId.Should().Be(tenantId);
    }

    private static TenantMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, Mock.Of<ILogger<TenantMiddleware>>());

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
}
