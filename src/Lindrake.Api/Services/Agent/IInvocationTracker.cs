// ============================================================
// Lindrake.Api — Plugin invocation tracking
// Scoped per-request collector (same pattern as ICitationAccumulator)
// fed by an auto-function-invocation filter on the request's Kernel.
// ============================================================

using Microsoft.SemanticKernel;

namespace Lindrake.Api.Services.Agent;

/// <summary>Per-request record of the kernel plugins the model invoked.</summary>
public interface IInvocationTracker
{
    /// <summary>Record that a function of <paramref name="pluginName"/> was invoked.</summary>
    void Record(string pluginName);

    /// <summary>Distinct plugin names in first-invocation order.</summary>
    IReadOnlyList<string> PluginsUsed { get; }
}

/// <summary>Default in-memory implementation, registered as Scoped.</summary>
public sealed class InvocationTracker : IInvocationTracker
{
    // SK can run several tool calls of one response concurrently when
    // FunctionChoiceBehaviorOptions.AllowConcurrentInvocation is on.
    private readonly Lock _gate = new();
    private readonly List<string> _plugins = [];

    public void Record(string pluginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        lock (_gate)
        {
            if (!_plugins.Contains(pluginName, StringComparer.Ordinal))
                _plugins.Add(pluginName);
        }
    }

    public IReadOnlyList<string> PluginsUsed
    {
        get
        {
            lock (_gate)
                return [.. _plugins];
        }
    }
}

/// <summary>
/// Reports every function the model auto-invokes to the request's
/// <see cref="IInvocationTracker"/>. Registered on the scoped Kernel.
/// </summary>
public sealed class PluginInvocationTrackingFilter(IInvocationTracker tracker) : IAutoFunctionInvocationFilter
{
    public Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // Recorded before invoking: a plugin that was called but failed was still used.
        tracker.Record(context.Function.PluginName ?? context.Function.Name);
        return next(context);
    }
}
