// ============================================================
// Integration Tests — audit trail output
// GET /api/audit and the audit section of GET /api/account/export
// return every AuditLog field, newest first, for the caller's
// tenant only.
// ============================================================

using System.Net.Http.Json;
using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Models;
using Lindrake.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace Lindrake.IntegrationTests;

public sealed class AuditEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuditEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAuditLog_ReturnsEveryField_NewestFirst_ForTheCallersTenantOnly()
    {
        var tenantId = Guid.NewGuid();
        var (older, newer) = await SeedAsync(tenantId);
        await SeedAsync(Guid.NewGuid());

        var entries = await _factory.CreateTenantClient(tenantId).GetFromJsonAsync<List<AuditLogDto>>("/api/audit");

        entries.Should().Equal(ToDto(newer), ToDto(older));
    }

    [Fact]
    public async Task Export_AuditLog_ReturnsEveryField_ForTheCallersTenantOnly()
    {
        var tenantId = Guid.NewGuid();
        var (older, newer) = await SeedAsync(tenantId);
        await SeedAsync(Guid.NewGuid());

        var export = await _factory.CreateTenantClient(tenantId).GetFromJsonAsync<TenantExportDto>("/api/account/export");

        export!.AuditLog.Should().BeEquivalentTo([ToDto(older), ToDto(newer)]);
    }

    private static AuditLogDto ToDto(AuditLog a) => new()
    {
        Id = a.Id,
        TenantId = a.TenantId,
        UserId = a.UserId,
        Action = a.Action,
        ResourceType = a.ResourceType,
        ResourceId = a.ResourceId,
        IpAddress = a.IpAddress,
        CreatedAt = a.CreatedAt
    };

    /// <summary>Two audit rows with every field set, a day apart.</summary>
    private async Task<(AuditLog Older, AuditLog Newer)> SeedAsync(Guid tenantId)
    {
        var older = new AuditLog
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid(),
            Action = "upload",
            ResourceType = "data_source",
            ResourceId = Guid.NewGuid(),
            IpAddress = "203.0.113.7",
            CreatedAt = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc)
        };
        var newer = new AuditLog
        {
            TenantId = tenantId,
            Action = "delete",
            ResourceType = "data_source",
            ResourceId = older.ResourceId,
            CreatedAt = older.CreatedAt.AddDays(1)
        };

        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        db.AuditLogs.AddRange(older, newer);
        await db.SaveChangesAsync();
        return (older, newer);
    }
}
