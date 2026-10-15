// ============================================================
// Integration Tests (Postgres) — account data erase
// DELETE /api/account/data on a relational provider takes the
// set-based ExecuteDelete path in one transaction (the InMemory
// suite covers only the load-and-remove fallback).
// ============================================================

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lindrake.Shared.DTOs;

namespace Lindrake.IntegrationTests.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class AccountEraseTests : IClassFixture<PublicSchemaPostgresFixture>
{
    private readonly PublicSchemaPostgresFixture _fixture;

    public AccountEraseTests(PublicSchemaPostgresFixture fixture) => _fixture = fixture;

    [PostgresFact]
    public async Task DeleteAccountData_ErasesTheTenantsRows_AuditsIt_AndLeavesOtherTenantsAlone()
    {
        var erased = await PostgresSeed.TenantWithDataAsync(_fixture.Factory, chunkCount: 3);
        var kept = await PostgresSeed.TenantWithDataAsync(_fixture.Factory, chunkCount: 2);

        var response = await _fixture.Factory.CreateTenantClient(erased.TenantId)
            .SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account/data")
            {
                Content = JsonContent.Create(new DeleteAccountDataRequest { Confirm = "DELETE" })
            });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fixture.CountAsSuperuserAsync("data_sources", erased.TenantId)).Should().Be(0);
        (await _fixture.CountAsSuperuserAsync("document_chunks", erased.TenantId)).Should().Be(0);
        (await _fixture.CountAsSuperuserAsync("query_history", erased.TenantId)).Should().Be(0);
        (await _fixture.CountAsSuperuserAsync("audit_log", erased.TenantId)).Should().Be(1,
            "the old trail is gone and the erase recorded itself");
        (await _fixture.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM audit_log WHERE tenant_id = @tenant AND action = 'delete_account_data' " +
            "AND resource_type = 'account' AND resource_id = @tenant",
            ("tenant", erased.TenantId))).Should().Be(1);

        (await _fixture.CountAsSuperuserAsync("data_sources", kept.TenantId)).Should().Be(1);
        (await _fixture.CountAsSuperuserAsync("document_chunks", kept.TenantId)).Should().Be(2);
        (await _fixture.CountAsSuperuserAsync("query_history", kept.TenantId)).Should().Be(1);
        (await _fixture.CountAsSuperuserAsync("audit_log", kept.TenantId)).Should().Be(1);
    }
}
