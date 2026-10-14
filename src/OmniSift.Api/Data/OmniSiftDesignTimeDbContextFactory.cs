using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OmniSift.Api.Middleware;

namespace OmniSift.Api.Data;

/// <summary>
/// Creates <see cref="OmniSiftDbContext"/> instances for EF Core tooling.
/// </summary>
public sealed class OmniSiftDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OmniSiftDbContext>
{
    public OmniSiftDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Fallback = appsettings.Development.json's value: the compose db's
        // omnisift_app role with the .env.dev password.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=omnisift;Username=omnisift_app;Password=omnisift_dev";

        var options = new DbContextOptionsBuilder<OmniSiftDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(3);
            })
            .Options;

        // No tenant is bound: migrations and tooling never query tenant-owned rows.
        return new OmniSiftDbContext(options, new TenantContext());
    }
}
