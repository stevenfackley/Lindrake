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

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=omnisift;Username=omnisift;Password=omnisift";

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
