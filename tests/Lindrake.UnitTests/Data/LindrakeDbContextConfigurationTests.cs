using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Middleware;
using Lindrake.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.UnitTests.Data;

public sealed class LindrakeDbContextConfigurationTests
{
    [Fact]
    public void DesignTimeFactory_CreateDbContext_UsesNpgsqlProvider()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var isolatedDirectory = Directory.CreateTempSubdirectory();

        try
        {
            Directory.SetCurrentDirectory(isolatedDirectory.FullName);

            var factory = new LindrakeDesignTimeDbContextFactory();
            using var context = factory.CreateDbContext([]);

            context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            isolatedDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Model_ConfiguresPostgresExtensions_Indexes_AndColumnShapes()
    {
        var apiKeyHashIndex = new[] { nameof(Tenant.ApiKeyHash) };
        var tenantStatusIndex = new[] { nameof(DataSource.TenantId), nameof(DataSource.Status) };
        var queryHistoryIndex = new[] { nameof(QueryHistory.TenantId), nameof(QueryHistory.CreatedAt) };

        var options = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseNpgsql("Host=localhost;Database=lindrake_tests;Username=postgres;Password=postgres", npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(3);
            })
            .Options;

        using var context = new LindrakeDbContext(options, new TenantContext());
        var model = context.Model;
        var createScript = context.Database.GenerateCreateScript();

        createScript.Should().Contain("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"");
        createScript.Should().Contain("CREATE EXTENSION IF NOT EXISTS vector");

        var tenantEntity = model.FindEntityType(typeof(Tenant));
        tenantEntity.Should().NotBeNull();
        tenantEntity!.FindProperty(nameof(Tenant.ApiKeyHash))!.GetMaxLength().Should().Be(64);
        tenantEntity.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(apiKeyHashIndex));

        var dataSourceEntity = model.FindEntityType(typeof(DataSource));
        dataSourceEntity.Should().NotBeNull();
        dataSourceEntity!.FindProperty(nameof(DataSource.Metadata))!.GetColumnType().Should().Be("jsonb");
        dataSourceEntity.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(tenantStatusIndex));

        var documentChunkEntity = model.FindEntityType(typeof(DocumentChunk));
        documentChunkEntity.Should().NotBeNull();
        documentChunkEntity!.FindProperty(nameof(DocumentChunk.Embedding))!.GetColumnType().Should().Be("vector(384)");
        documentChunkEntity.FindProperty(nameof(DocumentChunk.Metadata))!.GetColumnType().Should().Be("jsonb");

        var queryHistoryEntity = model.FindEntityType(typeof(QueryHistory));
        queryHistoryEntity.Should().NotBeNull();
        queryHistoryEntity!.FindProperty(nameof(QueryHistory.PluginsUsed))!.GetColumnType().Should().Be("jsonb");
        queryHistoryEntity.FindProperty(nameof(QueryHistory.Sources))!.GetColumnType().Should().Be("jsonb");
        queryHistoryEntity.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(queryHistoryIndex));
    }
}
