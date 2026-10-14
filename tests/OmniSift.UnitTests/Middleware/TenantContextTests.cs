// ============================================================
// Unit Tests — TenantContext
// ============================================================

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using OmniSift.Api.Infrastructure;
using OmniSift.Api.Middleware;

namespace OmniSift.UnitTests.Middleware;

public sealed class TenantContextTests
{
    [Fact]
    public void TenantId_WhenMiddlewareSetIt_ReturnsIt()
    {
        var tenantId = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenantId;

        new TenantContext(new HttpContextAccessor { HttpContext = http }).TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantId_WhenNotResolved_ThrowsTenantContextMissing()
    {
        var context = new TenantContext(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var act = () => context.TenantId;

        act.Should().Throw<TenantContextMissingException>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void TenantId_OutsideARequest_ThrowsTenantContextMissing()
    {
        var context = new TenantContext(new HttpContextAccessor());

        var act = () => context.TenantId;

        act.Should().Throw<TenantContextMissingException>();
    }
}
