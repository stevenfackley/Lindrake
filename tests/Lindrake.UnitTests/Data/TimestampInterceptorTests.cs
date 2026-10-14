// ============================================================
// Unit Tests — TimestampInterceptor
// ============================================================

using FluentAssertions;
using Lindrake.Api.Data;
using Lindrake.Api.Models;
using Lindrake.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Lindrake.UnitTests.Data;

public sealed class TimestampInterceptorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly string _databaseName = "timestamps-" + Guid.NewGuid();
    private readonly SettableTimeProvider _clock = new(T0);

    [Fact]
    public async Task Insert_StampsCreatedAndUpdatedFromTheClock()
    {
        await using var db = Db();
        var source = new DataSource { TenantId = TenantId, SourceType = "pdf" };
        var audit = new AuditLog { TenantId = TenantId, Action = "upload", ResourceType = "data_source" };
        db.AddRange(source, audit);

        await db.SaveChangesAsync();

        source.CreatedAt.Should().Be(T0.UtcDateTime);
        source.UpdatedAt.Should().Be(T0.UtcDateTime);
        audit.CreatedAt.Should().Be(T0.UtcDateTime);
    }

    [Fact]
    public async Task Insert_KeepsAnExplicitCreatedAt()
    {
        var backdated = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = Db();
        var source = new DataSource { TenantId = TenantId, SourceType = "pdf", CreatedAt = backdated };
        db.DataSources.Add(source);

        await db.SaveChangesAsync();

        source.CreatedAt.Should().Be(backdated);
        source.UpdatedAt.Should().Be(backdated);
    }

    [Fact]
    public async Task Update_AdvancesUpdatedAt_AndNeverRewritesCreatedAt()
    {
        var source = new DataSource { TenantId = TenantId, SourceType = "pdf" };
        await using (var seed = Db())
        {
            seed.DataSources.Add(source);
            await seed.SaveChangesAsync();
        }

        var later = T0.AddMinutes(5);
        _clock.UtcNow = later;
        await using (var db = Db())
        {
            var loaded = await db.DataSources.SingleAsync();
            loaded.Status = IngestionStatus.Completed;
            loaded.CreatedAt = later.UtcDateTime; // must not stick
            db.SaveChanges();
        }

        await using var verify = Db();
        var saved = await verify.DataSources.SingleAsync();
        saved.CreatedAt.Should().Be(T0.UtcDateTime);
        saved.UpdatedAt.Should().Be(later.UtcDateTime);
    }

    [Fact]
    public async Task UnchangedEntities_AreNotRestamped()
    {
        await using var db = Db();
        var source = new DataSource { TenantId = TenantId, SourceType = "pdf" };
        db.DataSources.Add(source);
        await db.SaveChangesAsync();

        _clock.UtcNow = T0.AddHours(1);
        await db.SaveChangesAsync();

        source.UpdatedAt.Should().Be(T0.UtcDateTime);
    }

    private LindrakeDbContext Db() =>
        InMemoryDb.ForTenant(_databaseName, TenantId, new TimestampInterceptor(_clock));

    private sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
