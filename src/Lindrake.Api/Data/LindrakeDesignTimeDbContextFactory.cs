using Lindrake.Api.Middleware;
using Lindrake.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lindrake.Api.Data;

/// <summary>
/// Creates <see cref="LindrakeDbContext"/> instances for EF Core tooling.
/// </summary>
public sealed class LindrakeDesignTimeDbContextFactory : IDesignTimeDbContextFactory<LindrakeDbContext>
{
    public LindrakeDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Fallback = appsettings.Development.json's value: the compose db's
        // lindrake_app role with the .env.dev password.
        var connectionString = configuration.GetConnectionString(LindrakeDatabase.DefaultConnectionName)
            ?? "Host=localhost;Port=5432;Database=lindrake;Username=lindrake_app;Password=lindrake_dev";

        var database = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var options = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseLindrakeNpgsql(connectionString, database)
            .Options;

        // No tenant is bound: migrations and tooling never query tenant-owned rows.
        return new LindrakeDbContext(options, new TenantContext());
    }
}
