// ============================================================
// Unit Tests — TenantMiddleware
// Verifies tenant is derived from the authenticated claim, not a header
// ============================================================

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Lindrake.Api.Infrastructure;
using Lindrake.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lindrake.UnitTests.Middleware;

public sealed class TenantMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AllowAnonymousEndpoint_SkipsTenantResolution()
    {
        // e.g. /health or /api/auth/login called with a token that has no tenant.
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/health");
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AllowAnonymousAttribute()), "anon"));
        context.User = Authenticated(new Claim("sub", Guid.NewGuid().ToString()));

        await middleware.InvokeAsync(context, new TenantContext());

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedRequest_CallsNextWithoutTenantContext()
    {
        // Authorization is responsible for the 401 on protected endpoints; the
        // middleware must not itself reject — it just continues without a tenant.
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/datasources");
        var tenantContext = new TenantContext();

        await middleware.InvokeAsync(context, tenantContext);

        nextCalled.Should().BeTrue();
        var tenant = () => tenantContext.TenantId;
        tenant.Should().Throw<TenantContextMissingException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvokeAsync_AuthenticatedWithoutValidTenantClaim_Returns403ProblemDetails(string? claimValue)
    {
        var nextCalled = false;
        var logger = new Mock<ILogger<TenantMiddleware>>();
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, logger.Object);
        var tenantContext = new TenantContext();
        var context = CreateContext("/api/datasources");
        context.User = claimValue is null
            ? Authenticated(new Claim("sub", Guid.NewGuid().ToString()))
            : Authenticated(new Claim("tenant_id", claimValue));

        await middleware.InvokeAsync(context, tenantContext);

        nextCalled.Should().BeFalse();
        tenantContext.CurrentTenantId.Should().BeNull();
        logger.Verify(
            l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().StartWith("application/problem+json");

        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(403);
        problem.RootElement.GetProperty("detail").GetString().Should().Be(TenantMiddleware.MissingTenantDetail);
        problem.RootElement.TryGetProperty("error", out _).Should().BeFalse();
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

        await middleware.InvokeAsync(context, tenantContext);

        nextCalled.Should().BeTrue();
        tenantContext.TenantId.Should().Be(tenantId);
    }

    private static TenantMiddleware CreateMiddleware(RequestDelegate next, ILogger<TenantMiddleware>? logger = null) =>
        new(next, logger ?? Mock.Of<ILogger<TenantMiddleware>>());

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
