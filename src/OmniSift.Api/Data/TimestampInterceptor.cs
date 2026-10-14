// ============================================================
// OmniSift.Api — CreatedAt / UpdatedAt stamping on save
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OmniSift.Api.Models;

namespace OmniSift.Api.Data;

/// <summary>
/// Stamps <see cref="ICreationTimestamped.CreatedAt"/> and
/// <see cref="ITimestamped.UpdatedAt"/> from <see cref="TimeProvider"/> as changes are
/// saved, so no call site sets them (or reads <see cref="DateTime.UtcNow"/>) by hand.
/// <list type="bullet">
/// <item>Added: <c>CreatedAt</c> = now unless already set (explicit back-dating,
/// e.g. imports or test seeds, is kept); <c>UpdatedAt</c> = <c>CreatedAt</c>.</item>
/// <item>Modified: <c>UpdatedAt</c> = now; <c>CreatedAt</c> is never written.</item>
/// </list>
/// Stateless apart from the clock: one instance serves every context.
/// </summary>
public sealed class TimestampInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<ICreationTimestamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                        entry.Entity.CreatedAt = now;
                    if (entry.Entity is ITimestamped added)
                        added.UpdatedAt = added.CreatedAt;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(ICreationTimestamped.CreatedAt)).IsModified = false;
                    if (entry.Entity is ITimestamped modified)
                        modified.UpdatedAt = now;
                    break;
            }
        }
    }
}
