// ============================================================
// Integration Tests — Postgres suite: skip unless configured
// ============================================================

namespace Lindrake.IntegrationTests.Postgres;

/// <summary>
/// A <see cref="FactAttribute"/> that is skipped, with a reason saying how to enable
/// it, unless <c>ConnectionStrings__PostgreSQL</c> names a Postgres server. CI's
/// shared .NET lane sets it (<c>run-postgres: true</c>); locally, point it at the
/// compose <c>db</c> (see CONTRIBUTING.md).
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresFixture.IsConfigured)
            Skip = PostgresFixture.SkipReason;
    }
}
