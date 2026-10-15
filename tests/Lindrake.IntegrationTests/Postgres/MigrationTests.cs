// ============================================================
// Integration Tests (Postgres) — migrations
// The existing migrations, unchanged, applied by the API's startup
// code as a non-superuser role, in both layouts: the local compose
// db (public) and qavren-db (one schema per app, extensions in
// "extensions", no CREATE on public).
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lindrake.IntegrationTests.Postgres;

public abstract class MigrationTests<TFixture>
    where TFixture : PostgresFixture
{
    private static readonly string[] Tables =
        ["tenants", "data_sources", "document_chunks", "query_history", "audit_log", "users", LindrakeDatabase.MigrationsHistoryTable];

    private readonly TFixture _fixture;

    protected MigrationTests(TFixture fixture) => _fixture = fixture;

    [PostgresFact]
    public async Task FreshDatabase_HasEveryMigrationApplied_AndNoPendingModelChanges()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LindrakeDbContext>();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        db.Database.HasPendingModelChanges().Should().BeFalse("the model snapshot must match the model");
    }

    [PostgresFact]
    public async Task MigratingAgain_IsANoOp()
    {
        var before = await HistoryRowsAsync();

        _fixture.Factory.Services.MigrateLindrakeDatabase();

        (await HistoryRowsAsync()).Should().Be(before);
    }

    [PostgresFact]
    public async Task EverythingTheAppRoleCreated_IsInTheConfiguredSchema()
    {
        var tables = new List<string>();
        await using (var connection = await PostgresFixture.OpenAsync(_fixture.SuperuserConnectionString))
        await using (var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema", connection))
        {
            command.Parameters.AddWithValue("schema", _fixture.Schema);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        tables.Should().Contain(Tables);
        (await _fixture.ScalarAsSuperuserAsync<long>(
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE pg_get_userbyid(c.relowner) = @role AND n.nspname NOT IN (@schema, 'pg_toast')",
            ("role", _fixture.AppRole), ("schema", _fixture.Schema))).Should().Be(0);
    }

    [PostgresFact]
    public async Task EveryTenantTable_HasRowLevelSecurityForced()
    {
        foreach (var table in RowLevelSecurityTests.TenantTables)
        {
            (await _fixture.ScalarAsSuperuserAsync<bool>(
                "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class " +
                "WHERE oid = to_regclass(quote_ident(@schema) || '.' || quote_ident(@table))",
                ("schema", _fixture.Schema), ("table", table))).Should().BeTrue(table);
        }
    }

    [PostgresFact]
    public void Migrate_WithMigrationsConnectionString_MigratesOverIt()
    {
        // A runtime connection that cannot log in: if migrations used it, Migrate would
        // fail fast (28P01 is not retried).
        var brokenRuntime = new NpgsqlConnectionStringBuilder(_fixture.AppConnectionString) { Password = "wrong" };
        using var factory = _fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:" + LindrakeDatabase.DefaultConnectionName] = brokenRuntime.ConnectionString,
            ["ConnectionStrings:" + LindrakeDatabase.MigrationsConnectionName] = _fixture.AppConnectionString,
        });

        FluentActions.Invoking(() => factory.Services.MigrateLindrakeDatabase()).Should().NotThrow();
    }

    [PostgresFact]
    public void Migrate_WhenDatabaseSchemaDisagreesWithTheSearchPath_Refuses()
    {
        using var factory = _fixture.CreateFactory(new Dictionary<string, string?>
        {
            [DatabaseOptions.Section + ":" + nameof(DatabaseOptions.Schema)] = "elsewhere",
        });

        FluentActions.Invoking(() => factory.Services.MigrateLindrakeDatabase())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"Database:Schema is 'elsewhere' but*current_schema() is '{_fixture.Schema}'*");
    }

    private Task<long> HistoryRowsAsync() =>
        _fixture.ScalarAsSuperuserAsync<long>(
            $"SELECT count(*) FROM \"{_fixture.Schema}\".\"{LindrakeDatabase.MigrationsHistoryTable}\"");
}

/// <summary>Local compose layout: tables and history in <c>public</c>.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PublicSchemaMigrationTests(PublicSchemaPostgresFixture fixture)
    : MigrationTests<PublicSchemaPostgresFixture>(fixture), IClassFixture<PublicSchemaPostgresFixture>;

/// <summary>qavren-db layout: tables and history in the app's own schema.</summary>
[Collection(PostgresCollection.Name)]
public sealed class QavrenSchemaMigrationTests(QavrenSchemaPostgresFixture fixture)
    : MigrationTests<QavrenSchemaPostgresFixture>(fixture), IClassFixture<QavrenSchemaPostgresFixture>;
