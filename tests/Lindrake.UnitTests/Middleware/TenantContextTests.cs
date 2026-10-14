// ============================================================
// Unit Tests — TenantContext
// ============================================================

using FluentAssertions;
using Lindrake.Api.Infrastructure;
using Lindrake.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace Lindrake.UnitTests.Middleware;

public sealed class TenantContextTests
{
    [Fact]
    public void TenantId_AfterSet_ReturnsIt()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext();

        context.Set(tenantId);

        context.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TenantId_WhenNotSet_ThrowsTenantContextMissing()
    {
        var act = () => new TenantContext().TenantId;

        act.Should().Throw<TenantContextMissingException>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void Set_SameTenantTwice_IsAllowed()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext();
        context.Set(tenantId);

        var act = () => context.Set(tenantId);

        act.Should().NotThrow();
    }

    [Fact]
    public void Set_DifferentTenant_Throws()
    {
        var context = new TenantContext();
        context.Set(Guid.NewGuid());

        var act = () => context.Set(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Set_EmptyGuid_Throws()
    {
        var act = () => new TenantContext().Set(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
