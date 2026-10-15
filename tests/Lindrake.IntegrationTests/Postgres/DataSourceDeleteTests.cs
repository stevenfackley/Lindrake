// ============================================================
// Integration Tests (Postgres) — data source delete
// The controller removes only the data_sources row; its chunks go
// through the FK's ON DELETE CASCADE, which InMemory cannot show.
// ============================================================

using System.Net;
using FluentAssertions;

namespace Lindrake.IntegrationTests.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class DataSourceDeleteTests : IClassFixture<PublicSchemaPostgresFixture>
{
    private readonly PublicSchemaPostgresFixture _fixture;

    public DataSourceDeleteTests(PublicSchemaPostgresFixture fixture) => _fixture = fixture;

    [PostgresFact]
    public async Task Delete_CascadesToItsChunks_AndKeepsTheTenantsOtherSources()
    {
        var owner = await PostgresSeed.TenantWithDataAsync(_fixture.Factory, chunkCount: 3);
        var (keptSource, _) = await PostgresSeed.DataSourceAsync(_fixture.Factory, owner.TenantId, chunkCount: 2);

        var response = await _fixture.Factory.CreateTenantClient(owner.TenantId)
            .DeleteAsync($"/api/datasources/{owner.DataSourceId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ChunksOfAsync(owner.DataSourceId)).Should().Be(0, "ON DELETE CASCADE removes the chunks");
        (await _fixture.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM data_sources WHERE id = @id", ("id", owner.DataSourceId))).Should().Be(0);
        (await ChunksOfAsync(keptSource)).Should().Be(2);
        (await _fixture.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM audit_log WHERE action = 'delete' AND resource_id = @id",
            ("id", owner.DataSourceId))).Should().Be(1);
    }

    private Task<long> ChunksOfAsync(Guid dataSourceId) =>
        _fixture.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM document_chunks WHERE data_source_id = @id", ("id", dataSourceId));
}
