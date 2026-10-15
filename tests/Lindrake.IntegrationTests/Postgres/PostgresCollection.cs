// ============================================================
// Integration Tests — Postgres suite collection
// ============================================================

namespace Lindrake.IntegrationTests.Postgres;

/// <summary>
/// Every Postgres test class is in this collection, so they run one class at a
/// time (each on its own database, see <see cref="PostgresFixture"/>) while the
/// InMemory suite runs in parallel beside them. One class at a time keeps the
/// connections held against the shared server well under its limit.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection
{
    public const string Name = "Postgres";
}
