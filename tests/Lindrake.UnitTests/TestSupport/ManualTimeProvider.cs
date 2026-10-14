// ============================================================
// Unit Tests — a clock that only moves when told to
// ============================================================

namespace Lindrake.UnitTests.TestSupport;

/// <summary>
/// <see cref="TimeProvider"/> whose time advances only through <see cref="Advance"/>,
/// which also fires the one-shot timers that came due.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by)
    {
        Now += by;
        foreach (var timer in _timers.ToList())
            timer.FireIfDue(Now);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Now + dueTime;
            if (!owner._timers.Contains(this))
                owner._timers.Add(this);
            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            if (_due is { } due && due <= now)
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => owner._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
