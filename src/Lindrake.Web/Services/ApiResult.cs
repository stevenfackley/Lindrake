// ============================================================
// Lindrake.Web — API call outcomes
// ============================================================

using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Lindrake.Web.Services;

/// <summary>
/// Why an API call failed, with a message fit to show the user. <see cref="StatusCode"/>
/// is the HTTP status, or 0 when no response arrived (network failure, timeout).
/// </summary>
public sealed record ApiError(int StatusCode, string Message)
{
    /// <summary>HTTP 401: the token is missing, expired or rejected.</summary>
    public bool IsUnauthorized => StatusCode == (int)HttpStatusCode.Unauthorized;

    /// <summary>HTTP 404.</summary>
    public bool IsNotFound => StatusCode == (int)HttpStatusCode.NotFound;

    /// <summary>No response, a 5xx, or 429: retrying later may succeed.</summary>
    public bool IsTransient => StatusCode == 0 || StatusCode >= 500 || StatusCode == (int)HttpStatusCode.TooManyRequests;

    public static ApiError Unreachable() =>
        new(0, "Could not reach the Lindrake API. Check your connection and try again.");

    public static ApiError TimedOut() =>
        new(0, "The Lindrake API did not respond in time. Please try again.");

    public static ApiError InvalidResponse(int statusCode) =>
        new(statusCode, "The Lindrake API returned a response this app could not read.");
}

/// <summary>The outcome of an API call that returns no body.</summary>
public class ApiResult
{
    private static readonly ApiResult SuccessResult = new(null);

    protected ApiResult(ApiError? error) => Error = error;

    /// <summary>Why the call failed; <see langword="null"/> on success.</summary>
    public ApiError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static ApiResult Success() => SuccessResult;

    public static ApiResult Failure(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApiResult(error);
    }
}

/// <summary>The outcome of an API call that returns a <typeparamref name="T"/>.</summary>
public sealed class ApiResult<T> : ApiResult
{
    private readonly T? _value;

    private ApiResult(T? value, ApiError? error) : base(error) => _value = value;

    /// <summary>The response body.</summary>
    /// <exception cref="InvalidOperationException">The call failed; check <see cref="ApiResult.IsSuccess"/> first.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The API call failed: {Error.Message}");

    public static ApiResult<T> Success(T value) => new(value, null);

    public static new ApiResult<T> Failure(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApiResult<T>(default, error);
    }
}
