// ============================================================
// Unit Tests — AuditLogger
// ============================================================

using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OmniSift.Api.Middleware;
using OmniSift.Api.Services;
using OmniSift.UnitTests.TestSupport;

namespace OmniSift.UnitTests.Services;

public sealed class AuditLoggerTests
{
    private readonly string _databaseName = "audit-" + Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task LogAsync_AuthenticatedRequest_RecordsUserIdAndIp()
    {
        var userId = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth"))
        };
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        await LogAsync(http);

        await using var verify = InMemoryDb.Create(_databaseName);
        var entry = await verify.AuditLogs.SingleAsync();
        entry.UserId.Should().Be(userId);
        entry.TenantId.Should().Be(_tenantId);
        entry.IpAddress.Should().Be("203.0.113.7");
        entry.Action.Should().Be("delete");
    }

    [Fact]
    public async Task LogAsync_NoUserClaim_RecordsNullUserId()
    {
        await LogAsync(new DefaultHttpContext());

        await using var verify = InMemoryDb.Create(_databaseName);
        (await verify.AuditLogs.SingleAsync()).UserId.Should().BeNull();
    }

    private async Task LogAsync(HttpContext http)
    {
        var tenant = new TenantContext();
        tenant.Set(_tenantId);

        await using var db = InMemoryDb.Create(_databaseName);
        var sut = new AuditLogger(db, tenant, new HttpContextAccessor { HttpContext = http });

        await sut.LogAsync("delete", "data_source", Guid.NewGuid());
    }
}
