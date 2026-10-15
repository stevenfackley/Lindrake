// ============================================================
// Unit Tests — Npgsql configuration shared by every context
// Database:Schema places the migrations history table and
// Database:MaxPoolSize caps the pool (qavren-db session pooler).
// ============================================================

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Lindrake.UnitTests.Data;

public sealed class LindrakeDatabaseTests
{
    [Fact]
    public void UseLindrakeNpgsql_PutsTheHistoryTableInTheSchema_AndCapsThePool()
    {
        var options = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseLindrakeNpgsql(
                "Host=db.example;Database=postgres;Username=lindrake;Password=secret;Maximum Pool Size=50",
                new DatabaseOptions { Schema = "lindrake", MaxPoolSize = 7 })
            .Options;

        var relational = RelationalOptionsExtension.Extract(options);
        relational.MigrationsHistoryTableName.Should().Be("__EFMigrationsHistory");
        relational.MigrationsHistoryTableSchema.Should().Be("lindrake");
        new NpgsqlConnectionStringBuilder(relational.ConnectionString).MaxPoolSize
            .Should().Be(7, "Database:MaxPoolSize replaces the connection string's value");
    }

    [Fact]
    public void UseLindrakeNpgsql_Defaults_AreTheLocalComposeLayout()
    {
        var options = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseLindrakeNpgsql("Host=localhost;Database=lindrake", new DatabaseOptions())
            .Options;

        var relational = RelationalOptionsExtension.Extract(options);
        relational.MigrationsHistoryTableSchema.Should().Be("public");
        new NpgsqlConnectionStringBuilder(relational.ConnectionString).MaxPoolSize.Should().Be(10);
    }

    [Fact]
    public void UseLindrakeNpgsql_NoConnectionString_LeavesItUnset()
    {
        var options = new DbContextOptionsBuilder<LindrakeDbContext>()
            .UseLindrakeNpgsql(null, new DatabaseOptions())
            .Options;

        RelationalOptionsExtension.Extract(options).ConnectionString.Should().BeNull();
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("lindrake", true)]
    [InlineData("lindrake_qdb", true)]
    [InlineData("Lindrake", false)]
    [InlineData("lindrake-app", false)]
    [InlineData("lindrake; drop", false)]
    [InlineData("", false)]
    public void Schema_MustBeAnUnquotedIdentifier(string schema, bool valid)
    {
        IsValid(new DatabaseOptions { Schema = schema }).Should().Be(valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void MaxPoolSize_IsBounded(int maxPoolSize, bool valid)
    {
        IsValid(new DatabaseOptions { MaxPoolSize = maxPoolSize }).Should().Be(valid);
    }

    private static bool IsValid(DatabaseOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
