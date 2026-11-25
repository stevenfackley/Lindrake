// ============================================================
// Unit Tests — a clock that only moves when told to
// ============================================================

namespace Lindrake.UnitTests.TestSupport;

/// <summary>
/// <see cref="TimeProvider"/> whose time advances only through <see cref="Advance"/>,
/// which also fires the one-shot timers that came due. Safe across threads: the code
/// under test may arm its timers on a pool thread while the test moves the clock.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];
    private DateTimeOffset _now = start;

    public DateTimeOffset Now
    {
        get
        {
            lock (_timers)
                return _now;
        }
    }

    /// <summary>How many timers are waiting to fire.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_timers)
                return _timers.Count(t => t.IsPending);
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by)
    {
        ManualTimer[] timers;
        lock (_timers)
        {
            _now += by;
            timers = [.. _timers];
        }

        // Outside the lock: a callback may arm or dispose timers.
        foreach (var timer in timers)
            timer.FireIfDue();
    }

    /// <summary>
    /// Completes once at least <paramref name="count"/> timers are waiting to fire. This is
    /// how a test knows the code under test has gone to sleep before it moves the clock.
    /// </summary>
    public Task WhenTimersPendingAsync(int count)
    {
        lock (_timers)
        {
            if (PendingTimers >= count)
                return Task.CompletedTask;

            // Asynchronous continuations: the waiter must not resume inside the lock.
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, reached));
            return reached.Task;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    // Called with the lock held.
    private void ReleaseWaiters()
    {
        var pending = PendingTimers;
        foreach (var waiter in _waiters.Where(w => w.Count <= pending).ToList())
        {
            _waiters.Remove(waiter);
            waiter.Reached.SetResult();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool IsPending => _due is not null;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._timers)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                if (!owner._timers.Contains(this))
                    owner._timers.Add(this);
                owner.ReleaseWaiters();
            }

            return true;
        }

        public void FireIfDue()
        {
            lock (owner._timers)
            {
                if (_due is not { } due || due > owner._now)
                    return;
                _due = null;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (owner._timers)
                owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
