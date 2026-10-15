// ============================================================
// Lindrake.Api — Npgsql configuration and startup migrations
// One place for what every LindrakeDbContext connection shares
// (runtime, startup migrations, EF tooling).
// ============================================================

using Lindrake.Api.Middleware;
using Lindrake.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lindrake.Api.Data;

public static class LindrakeDatabase
{
    /// <summary>Runtime connection string name (<c>ConnectionStrings:DefaultConnection</c>).</summary>
    public const string DefaultConnectionName = "DefaultConnection";

    /// <summary>
    /// Optional connection string used only by startup migrations
    /// (<c>ConnectionStrings:Migrations</c>); falls back to <see cref="DefaultConnectionName"/>.
    /// </summary>
    public const string MigrationsConnectionName = "Migrations";

    /// <summary>EF's default history table name, kept so existing databases keep their history.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Npgsql with pgvector, retry on transient failures, the migrations history
    /// table in <see cref="DatabaseOptions.Schema"/>, and
    /// <see cref="DatabaseOptions.MaxPoolSize"/> applied to the connection string.
    /// <para>
    /// The model sets no default schema on purpose: <c>HasDefaultSchema</c> would
    /// change the model snapshot and make EF move every table. Tables are created
    /// unqualified and land in the connecting role's <c>search_path</c>.
    /// </para>
    /// </summary>
    public static TBuilder UseLindrakeNpgsql<TBuilder>(
        this TBuilder builder,
        string? connectionString,
        DatabaseOptions options)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.UseNpgsql(
            string.IsNullOrWhiteSpace(connectionString) ? null : WithPoolSize(connectionString, options),
            npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(3);
                npgsql.MigrationsHistoryTable(MigrationsHistoryTable, options.Schema);
            });

        return builder;
    }

    /// <summary>
    /// Applies the EF migrations as the migrations connection's role, after checking
    /// that its <c>current_schema()</c> is <see cref="DatabaseOptions.Schema"/>.
    /// Used by <c>InitializeLindrake</c> at startup and by the Postgres test suite.
    /// </summary>
    public static void MigrateLindrakeDatabase(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var configuration = services.GetRequiredService<IConfiguration>();
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var migrationsConnection = configuration.GetConnectionString(MigrationsConnectionName);

        if (string.IsNullOrWhiteSpace(migrationsConnection))
        {
            using var scope = services.CreateScope();
            Migrate(scope.ServiceProvider.GetRequiredService<LindrakeDbContext>(), options.Schema);
            return;
        }

        // A separate endpoint or role for DDL: same model and history table, no
        // interceptors (no tenant is bound while migrating).
        var migrationOptions = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseLindrakeNpgsql(migrationsConnection, options)
            .Options;
        using var context = new LindrakeDbContext(migrationOptions, new TenantContext());
        Migrate(context, options.Schema);
    }

    /// <summary>
    /// <paramref name="connectionString"/> with <c>Maximum Pool Size</c> set to
    /// <see cref="DatabaseOptions.MaxPoolSize"/>.
    /// </summary>
    internal static string WithPoolSize(string connectionString, DatabaseOptions options) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = options.MaxPoolSize }.ConnectionString;

    private static void Migrate(LindrakeDbContext context, string expectedSchema)
    {
        // Unqualified CREATE TABLEs go to current_schema(): refuse to scatter the
        // tables when the role's search_path and Database:Schema disagree.
        var currentSchema = context.Database
            .SqlQuery<string>($"SELECT current_schema() AS \"Value\"")
            .AsEnumerable()
            .SingleOrDefault();

        if (!string.Equals(currentSchema, expectedSchema, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Database:Schema is '{expectedSchema}' but the migrations connection's current_schema() is " +
                $"'{currentSchema ?? "(none)"}'. Set the role's search_path to start with '{expectedSchema}' " +
                "(qavren-db provisioning does) or correct Database:Schema.");

        context.Database.Migrate();
    }
}
