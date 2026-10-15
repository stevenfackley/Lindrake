// ============================================================
// Integration Tests — Postgres suite fixture
// One database per test class on the server named by
// ConnectionStrings__PostgreSQL, provisioned the way the target
// environment is (local compose init.sql, or qavren-db), migrated
// by the API's own startup code, and dropped afterwards.
// ============================================================

using Lindrake.Api.Data;
using Lindrake.Api.Options;
using Npgsql;

namespace Lindrake.IntegrationTests.Postgres;

/// <summary>
/// A fresh database per test class plus a <see cref="CustomWebApplicationFactory"/>
/// running the API against it with its real Npgsql registration.
/// <para>
/// The server connection (<c>ConnectionStrings__PostgreSQL</c>) must be a superuser:
/// it creates the database, the extensions and the app role. The API then connects as
/// that non-superuser, NOBYPASSRLS role, which also runs the migrations and so owns
/// the tables, as in every real environment; <c>FORCE ROW LEVEL SECURITY</c> binds
/// the owner too. Unset variable: nothing happens and every <see cref="PostgresFactAttribute"/>
/// test is skipped.
/// </para>
/// </summary>
public abstract class PostgresFixture : IAsyncLifetime
{
    public const string ConnectionStringVariable = "ConnectionStrings__PostgreSQL";

    public const string SkipReason =
        "Postgres suite: set ConnectionStrings__PostgreSQL to a superuser connection string " +
        "for a pgvector server (CI's run-postgres lane, or the compose db; see CONTRIBUTING.md).";

    /// <summary>
    /// Roles are cluster-wide and test classes in parallel processes may share a
    /// server: a session advisory lock serialises the check-then-create.
    /// </summary>
    private const long RoleSetupLockKey = 0x6C_69_6E_64_72_6B; // "lindrk"

    private const string AppRolePassword = "lindrake_test_password";

    /// <summary>The superuser connection to the server, or null when the suite is off.</summary>
    public static string? ServerConnectionString { get; } =
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;

    public static bool IsConfigured => ServerConnectionString is not null;

    /// <summary>The schema the tables land in, also passed to the API as <c>Database:Schema</c>.</summary>
    public abstract string Schema { get; }

    /// <summary>The non-superuser login role the API connects and migrates as.</summary>
    public abstract string AppRole { get; }

    /// <summary>Configuration layered over the factory's (e.g. a pool size).</summary>
    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings { get; } =
        new Dictionary<string, string?>();

    public string DatabaseName { get; } = "lindrake_it_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>This database as <see cref="AppRole"/>: RLS applies.</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// This database as the superuser, which bypasses RLS: ground truth for
    /// assertions, never for the code under test. Its search_path is <see cref="Schema"/>.
    /// </summary>
    public string SuperuserConnectionString { get; private set; } = string.Empty;

    /// <summary>The API on this database (migrated, seed tenant included).</summary>
    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>
    /// Superuser SQL that prepares the new database for <see cref="AppRole"/> before the
    /// API migrates it.
    /// </summary>
    protected abstract string ProvisionSql();

    public async Task InitializeAsync()
    {
        if (ServerConnectionString is not { } server)
            return;

        await using (var connection = await OpenAsync(server))
        {
            await ExecuteAsync(connection, $"SELECT pg_advisory_lock({RoleSetupLockKey})");
            try
            {
                await ExecuteAsync(connection, $"""
                    DO $$
                    BEGIN
                        IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                            CREATE ROLE {AppRole} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
                        END IF;
                    END
                    $$;
                    ALTER ROLE {AppRole} WITH LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{AppRolePassword}';
                    """);
            }
            finally
            {
                await ExecuteAsync(connection, $"SELECT pg_advisory_unlock({RoleSetupLockKey})");
            }

            // Owned by the superuser: the app role gets no database-level CREATE,
            // as on qavren-db.
            await ExecuteAsync(connection, $"CREATE DATABASE {DatabaseName}");
        }

        SuperuserConnectionString = new NpgsqlConnectionStringBuilder(server)
        {
            Database = DatabaseName,
            SearchPath = Schema,
        }.ConnectionString;

        await using (var connection = await OpenAsync(SuperuserConnectionString))
            await ExecuteAsync(connection, ProvisionSql());

        AppConnectionString = new NpgsqlConnectionStringBuilder(server)
        {
            Database = DatabaseName,
            Username = AppRole,
            Password = AppRolePassword,
        }.ConnectionString;

        Factory = CreateFactory(new Dictionary<string, string?>());

        // The same code InitializeLindrake runs at startup (skipped in Testing).
        Factory.Services.MigrateLindrakeDatabase();
    }

    /// <summary>
    /// Another API instance on this database with <paramref name="settings"/> layered
    /// over the fixture's. The caller disposes it.
    /// </summary>
    public CustomWebApplicationFactory CreateFactory(IReadOnlyDictionary<string, string?> settings)
    {
        var merged = new Dictionary<string, string?>
        {
            [DatabaseOptions.Section + ":" + nameof(DatabaseOptions.Schema)] = Schema,
        };
        foreach (var (key, value) in ExtraSettings.Concat(settings))
            merged[key] = value;

        return new CustomWebApplicationFactory
        {
            PostgresConnectionString = AppConnectionString,
            ExtraSettings = merged,
        };
    }

    /// <summary>A connection as <see cref="AppRole"/>, opened.</summary>
    public Task<NpgsqlConnection> OpenAsAppRoleAsync(bool pooling = true) =>
        OpenAsync(new NpgsqlConnectionStringBuilder(AppConnectionString) { Pooling = pooling }.ConnectionString);

    /// <summary>Runs a scalar query as the superuser (bypassing RLS).</summary>
    public async Task<T> ScalarAsSuperuserAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(SuperuserConnectionString);
        return await ScalarAsync<T>(connection, sql, parameters);
    }

    /// <summary>Row count of <paramref name="table"/> for one tenant, as the superuser.</summary>
    public Task<long> CountAsSuperuserAsync(string table, Guid tenantId) =>
        ScalarAsSuperuserAsync<long>($"SELECT count(*) FROM {table} WHERE tenant_id = @tenant", ("tenant", tenantId));

    public async Task DisposeAsync()
    {
        if (ServerConnectionString is not { } server)
            return;

        if (Factory is not null)
            await Factory.DisposeAsync();

        // Each class has its own database and so its own pools: close them, then drop
        // the database (FORCE ends any connection still open, e.g. in a data source
        // EF built), or finished classes keep holding the server's connection slots.
        if (AppConnectionString.Length > 0)
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(AppConnectionString));
            NpgsqlConnection.ClearPool(new NpgsqlConnection(SuperuserConnectionString));
        }

        await using var connection = await OpenAsync(server);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {DatabaseName} WITH (FORCE)");
    }

    internal static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    internal static async Task<int> ExecuteAsync(
        NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    internal static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}

/// <summary>
/// The local compose layout (<c>infra/db/init.sql</c>): extensions in <c>public</c>,
/// tables in <c>public</c>, the app role granted USAGE and CREATE there.
/// </summary>
public class PublicSchemaPostgresFixture : PostgresFixture
{
    public override string Schema => "public";

    public override string AppRole => "lindrake_test_app";

    protected override string ProvisionSql() => $"""
        CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
        CREATE EXTENSION IF NOT EXISTS vector;
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
        GRANT USAGE, CREATE ON SCHEMA public TO {AppRole};
        """;
}

/// <summary>
/// <see cref="PublicSchemaPostgresFixture"/> with <c>Database:MaxPoolSize=1</c>: every
/// scope of the API reuses one physical connection.
/// </summary>
public sealed class SingleConnectionPostgresFixture : PublicSchemaPostgresFixture
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings { get; } =
        new Dictionary<string, string?>
        {
            [DatabaseOptions.Section + ":" + nameof(DatabaseOptions.MaxPoolSize)] = "1",
        };
}

/// <summary>
/// The qavren-db layout (<c>qavren-db/sql/provision.sql.tmpl</c> on a Supabase-style
/// cluster): extensions in an <c>extensions</c> schema, one schema named like the app
/// role and owned by it, the role's search_path <c>&lt;schema&gt;, extensions</c>,
/// no CREATE on <c>public</c>. The role is not called <c>lindrake</c> because that is
/// the compose db's superuser; only the name differs from production.
/// </summary>
public sealed class QavrenSchemaPostgresFixture : PostgresFixture
{
    public override string Schema => "lindrake_qdb";

    public override string AppRole => "lindrake_qdb";

    protected override string ProvisionSql() => $"""
        CREATE SCHEMA extensions;
        CREATE EXTENSION "uuid-ossp" WITH SCHEMA extensions;
        CREATE EXTENSION vector WITH SCHEMA extensions;
        CREATE EXTENSION pg_trgm WITH SCHEMA extensions;

        REVOKE CREATE ON SCHEMA public FROM PUBLIC;
        ALTER ROLE {AppRole} IN DATABASE {DatabaseName} SET search_path = {Schema}, extensions;
        CREATE SCHEMA {Schema} AUTHORIZATION {AppRole};
        REVOKE ALL ON SCHEMA public FROM {AppRole};
        GRANT USAGE ON SCHEMA extensions TO {AppRole};
        """;
}
