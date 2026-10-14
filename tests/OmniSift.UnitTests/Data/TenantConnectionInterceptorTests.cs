// ============================================================
// Unit Tests — TenantConnectionInterceptor
// Drives the interceptor with a recording DbConnection: set_config
// must run on open exactly when the context's tenant is bound.
// ============================================================

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OmniSift.Api.Data;
using OmniSift.Api.Extensions;
using OmniSift.Api.Middleware;

namespace OmniSift.UnitTests.Data;

public sealed class TenantConnectionInterceptorTests
{
    private readonly TenantConnectionInterceptor _interceptor = new();

    [Fact]
    public async Task ConnectionOpenedAsync_TenantBound_SetsTheSessionTenant()
    {
        var tenantId = Guid.NewGuid();
        using var db = Context(Bound(tenantId));
        using var connection = new RecordingConnection();

        await _interceptor.ConnectionOpenedAsync(connection, Opened(connection, db));

        var command = connection.Executed.Should().ContainSingle().Subject;
        command.CommandText.Should().Be(TenantConnectionInterceptor.SetTenantSql);
        var parameter = command.Parameters.Cast<DbParameter>().Should().ContainSingle().Subject;
        parameter.ParameterName.Should().Be("@tenantId");
        parameter.Value.Should().Be(tenantId.ToString());
    }

    [Fact]
    public void ConnectionOpened_TenantBound_SetsTheSessionTenant()
    {
        var tenantId = Guid.NewGuid();
        using var db = Context(Bound(tenantId));
        using var connection = new RecordingConnection();

        _interceptor.ConnectionOpened(connection, Opened(connection, db));

        connection.Executed.Should().ContainSingle()
            .Which.Parameters[0].Value.Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task ConnectionOpened_NoTenantBound_IssuesNothing()
    {
        using var db = Context(new TenantContext());
        using var connection = new RecordingConnection();

        await _interceptor.ConnectionOpenedAsync(connection, Opened(connection, db));
        _interceptor.ConnectionOpened(connection, Opened(connection, db));

        connection.Executed.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectionOpened_NotAnOmniSiftContext_IssuesNothing()
    {
        using var connection = new RecordingConnection();

        await _interceptor.ConnectionOpenedAsync(connection, Opened(connection, context: null));

        connection.Executed.Should().BeEmpty();
    }

    [Fact]
    public void AddOmniSiftPersistence_RegistersTheInterceptorOnTheContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=unused.invalid;Database=omnisift"
            })
            .Build();
        using var services = new ServiceCollection().AddOmniSiftPersistence(configuration).BuildServiceProvider();
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<OmniSiftDbContext>();

        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors
            .Should().ContainSingle(i => i is TenantConnectionInterceptor);
    }

    private static TenantContext Bound(Guid tenantId)
    {
        var tenant = new TenantContext();
        tenant.Set(tenantId);
        return tenant;
    }

    private static OmniSiftDbContext Context(ITenantContext tenant) =>
        new(new DbContextOptionsBuilder<OmniSiftDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=omnisift", npgsql => npgsql.UseVector())
            .Options, tenant);

    private static ConnectionEndEventData Opened(DbConnection connection, DbContext? context) =>
        new(
            eventDefinition: null!,
            messageGenerator: null!,
            connection,
            context,
            connectionId: Guid.NewGuid(),
            async: true,
            startTime: DateTimeOffset.UtcNow,
            duration: TimeSpan.Zero);

    /// <summary>A DbConnection that records the commands executed on it.</summary>
    private sealed class RecordingConnection : DbConnection
    {
        public List<DbCommand> Executed { get; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "omnisift";
        public override string DataSource => "recording";
        public override string ServerVersion => "16";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this);
    }

    /// <summary>Records itself on execution; parameters come from a real NpgsqlCommand.</summary>
    private sealed class RecordingCommand(RecordingConnection connection) : DbCommand
    {
        private readonly NpgsqlCommand _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override int ExecuteNonQuery()
        {
            connection.Executed.Add(this);
            return 1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _parameters.Dispose();
            base.Dispose(disposing);
        }
    }
}
