// ============================================================
// Integration Tests — Account (GDPR) endpoints
// ============================================================

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Models;
using Lindrake.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lindrake.IntegrationTests;

public sealed class AccountEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AccountEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task DeleteAccountData_ErasesTheTenantsData_AndAuditsTheErase()
    {
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        await SeedAsync(tenantId);
        await SeedAsync(otherTenant);

        var response = await DeleteAsync(tenantId, "DELETE");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
            (await db.DataSources.CountAsync()).Should().Be(0);
            (await db.DocumentChunks.CountAsync()).Should().Be(0);
            (await db.QueryHistories.CountAsync()).Should().Be(0);

            // The erase removes the old audit trail and records itself in it.
            var audit = await db.AuditLogs.SingleAsync();
            audit.Action.Should().Be("delete_account_data");
            audit.ResourceType.Should().Be("account");
            audit.ResourceId.Should().Be(tenantId);
        }

        using (var scope = _factory.CreateTenantScope(otherTenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
            (await db.DataSources.CountAsync()).Should().Be(1);
            (await db.DocumentChunks.CountAsync()).Should().Be(1);
            (await db.QueryHistories.CountAsync()).Should().Be(1);
            (await db.AuditLogs.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task DeleteAccountData_WithoutConfirmation_Returns400AndDeletesNothing()
    {
        var tenantId = Guid.NewGuid();
        await SeedAsync(tenantId);

        var response = await DeleteAsync(tenantId, "delete");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        (await db.DataSources.CountAsync()).Should().Be(1);
    }

    private Task<HttpResponseMessage> DeleteAsync(Guid tenantId, string confirm) =>
        _factory.CreateTenantClient(tenantId).SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account/data")
        {
            Content = JsonContent.Create(new DeleteAccountDataRequest { Confirm = confirm })
        });

    private async Task SeedAsync(Guid tenantId)
    {
        using var scope = _factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
        var source = new DataSource { TenantId = tenantId, SourceType = "pdf", FileName = "doc.pdf" };
        db.DataSources.Add(source);
        db.DocumentChunks.Add(new DocumentChunk { TenantId = tenantId, DataSourceId = source.Id, Content = "text" });
        db.QueryHistories.Add(new QueryHistory { TenantId = tenantId, QueryText = "q" });
        db.AuditLogs.Add(new AuditLog { TenantId = tenantId, Action = "upload", ResourceType = "data_source" });
        await db.SaveChangesAsync();
    }
}
