// ============================================================
// Integration Tests (Postgres) — Row-Level Security
// The InMemory suite only sees the EF tenant filter. Here the
// second wall is real: policies keyed on app.current_tenant, the
// app role bound by FORCE ROW LEVEL SECURITY, audit_log
// append-only, users deliberately without RLS.
// ============================================================

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Lindrake.IntegrationTests.ProblemAssertions;

namespace Lindrake.IntegrationTests.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class RowLevelSecurityTests : IClassFixture<PublicSchemaPostgresFixture>
{
    /// <summary>Every table with tenant RLS policies.</summary>
    internal static readonly string[] TenantTables = ["data_sources", "document_chunks", "query_history", "audit_log"];

    private readonly PublicSchemaPostgresFixture _fixture;

    public RowLevelSecurityTests(PublicSchemaPostgresFixture fixture) => _fixture = fixture;

    private CustomWebApplicationFactory Factory => _fixture.Factory;

    [PostgresFact]
    public async Task OtherTenant_OverHttp_GetsNotFoundAndEmptyResults()
    {
        var owner = await PostgresSeed.TenantWithDataAsync(Factory);
        var other = Factory.CreateTenantClient(await PostgresSeed.TenantAsync(Factory));

        await ShouldBeProblemAsync(await other.GetAsync($"/api/datasources/{owner.DataSourceId}"), HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<List<DataSourceDto>>("/api/datasources")).Should().BeEmpty();
        (await other.GetFromJsonAsync<List<AuditLogDto>>("/api/audit")).Should().BeEmpty();
        var otherExport = await other.GetFromJsonAsync<TenantExportDto>("/api/account/export");
        otherExport!.DataSources.Should().BeEmpty();
        otherExport.QueryHistory.Should().BeEmpty();
        otherExport.AuditLog.Should().BeEmpty();

        // The owner reads the same rows through the same endpoints, so the empty
        // answers above are isolation, not a failed seed.
        var ownerExport = await Factory.CreateTenantClient(owner.TenantId)
            .GetFromJsonAsync<TenantExportDto>("/api/account/export");
        ownerExport!.DataSources.Should().ContainSingle(s => s.Id == owner.DataSourceId && s.ChunkCount == 2);
        ownerExport.QueryHistory.Should().ContainSingle(q => q.Id == owner.QueryHistoryId);
        ownerExport.AuditLog.Should().ContainSingle(a => a.Id == owner.AuditLogId);
    }

    [PostgresFact]
    public async Task AppRole_BoundToAnotherTenant_SeesNoneOfTheRows()
    {
        var owner = await PostgresSeed.TenantWithDataAsync(Factory);
        await using var connection = await _fixture.OpenAsAppRoleAsync(pooling: false);

        await SetTenantAsync(connection, Guid.NewGuid());
        foreach (var table in TenantTables)
            (await CountAsync(connection, table, owner.TenantId)).Should().Be(0, $"{table} is behind RLS");

        await SetTenantAsync(connection, owner.TenantId);
        foreach (var table in TenantTables)
            (await CountAsync(connection, table, owner.TenantId)).Should().BePositive($"the owner sees its own {table} rows");
    }

    [PostgresFact]
    public async Task AppRole_WithNoTenantSetting_SeesNoRows()
    {
        var owner = await PostgresSeed.TenantWithDataAsync(Factory);

        // Unpooled: a brand-new session on which app.current_tenant was never set.
        await using var connection = await _fixture.OpenAsAppRoleAsync(pooling: false);

        foreach (var table in TenantTables)
        {
            (await CountAsync(connection, table, tenantId: null)).Should().Be(0, $"{table} fails closed");
            (await _fixture.CountAsSuperuserAsync(table, owner.TenantId)).Should().BePositive();
        }
    }

    [PostgresFact]
    public async Task AppRole_AfterThePoolResetsTheSession_SeesNoRows()
    {
        var owner = await PostgresSeed.TenantWithDataAsync(Factory);
        await using var connection = await _fixture.OpenAsAppRoleAsync(pooling: false);

        // What Npgsql does before reusing a pooled connection. Once set in a session,
        // the setting reads back as '' (not NULL) after the reset.
        await SetTenantAsync(connection, owner.TenantId);
        await PostgresFixture.ExecuteAsync(connection, "DISCARD ALL");
        (await PostgresFixture.ScalarAsync<string>(connection,
            "SELECT current_setting('app.current_tenant', true)")).Should().BeEmpty();

        foreach (var table in TenantTables)
            (await CountAsync(connection, table, tenantId: null)).Should().Be(0, $"{table} fails closed");
    }

    [PostgresFact]
    public async Task AuditLogUpdate_AsAppRole_IsDenied()
    {
        var owner = await PostgresSeed.TenantWithDataAsync(Factory);

        // Raw SQL, bound to the row's own tenant: no UPDATE policy means no row qualifies.
        await using (var connection = await _fixture.OpenAsAppRoleAsync(pooling: false))
        {
            await SetTenantAsync(connection, owner.TenantId);
            var updated = await PostgresFixture.ExecuteAsync(connection,
                "UPDATE audit_log SET action = 'tampered' WHERE id = @id", ("id", owner.AuditLogId));
            updated.Should().Be(0);
        }

        // Through the API's DbContext: EF sees zero rows affected.
        using (var scope = Factory.CreateTenantScope(owner.TenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();
            var entry = await db.AuditLogs.SingleAsync(a => a.Id == owner.AuditLogId);
            entry.Action = "tampered";

            await FluentActions.Awaiting(() => db.SaveChangesAsync())
                .Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        (await _fixture.ScalarAsSuperuserAsync<string>(
            "SELECT action FROM audit_log WHERE id = @id", ("id", owner.AuditLogId))).Should().Be("upload");
    }

    [PostgresFact]
    public async Task Register_EmailUsedByAnotherTenant_Returns409()
    {
        // users has no RLS on purpose: the duplicate check must see every tenant's rows.
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        var first = await RegisterAsync(email, "First Family Archive");
        var second = await RegisterAsync(email, "Second Family Archive");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldBeProblemAsync(second, HttpStatusCode.Conflict);
        (await _fixture.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM users WHERE email = @email", ("email", email))).Should().Be(1);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email, string tenantName) =>
        Factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Password = "correct-horse-battery",
            TenantName = tenantName,
        });

    /// <summary>What <see cref="TenantConnectionInterceptor"/> runs on every open.</summary>
    internal static Task<int> SetTenantAsync(NpgsqlConnection connection, Guid tenantId) =>
        PostgresFixture.ExecuteAsync(connection,
            "SELECT set_config('app.current_tenant', @tenant, false)", ("tenant", tenantId.ToString()));

    /// <summary>Rows of <paramref name="table"/> this connection can see (one tenant's, or all).</summary>
    internal static Task<long> CountAsync(NpgsqlConnection connection, string table, Guid? tenantId) =>
        tenantId is { } id
            ? PostgresFixture.ScalarAsync<long>(connection, $"SELECT count(*) FROM {table} WHERE tenant_id = @tenant", ("tenant", id))
            : PostgresFixture.ScalarAsync<long>(connection, $"SELECT count(*) FROM {table}");
}
