// ============================================================
// Unit Tests — IJSRuntime backed by an in-memory localStorage
// ============================================================

using Microsoft.JSInterop;

namespace Lindrake.UnitTests.TestSupport;

/// <summary>Implements the <c>localStorage.*</c> calls AuthStateService makes.</summary>
internal sealed class FakeJsRuntime : IJSRuntime
{
    public Dictionary<string, string> LocalStorage { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var key = (string)args![0]!;
        switch (identifier)
        {
            case "localStorage.getItem":
                return ValueTask.FromResult((TValue)(object?)LocalStorage.GetValueOrDefault(key)!);
            case "localStorage.setItem":
                LocalStorage[key] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            case "localStorage.removeItem":
                LocalStorage.Remove(key);
                return ValueTask.FromResult(default(TValue)!);
            default:
                throw new NotSupportedException($"FakeJsRuntime does not implement '{identifier}'.");
        }
    }
}
