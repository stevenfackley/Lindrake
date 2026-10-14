// ============================================================
// OmniSift.Api — Global Exception Handler
// Catches unhandled exceptions and returns RFC 7807 ProblemDetails
// ============================================================

using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace OmniSift.Api.Infrastructure;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 ProblemDetails.
/// <list type="bullet">
/// <item><see cref="OmniSiftException"/>: its status code and message (expected, client-facing).</item>
/// <item><see cref="BadHttpRequestException"/>: its status code (malformed / oversized request).</item>
/// <item><see cref="HttpRequestException"/>: 502, generic detail (upstream fetch failed).</item>
/// <item>Anything else: 500, generic detail. The message is logged, never returned.</item>
/// </list>
/// Every response carries a <c>traceId</c> extension to correlate with the logs.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Let cancellation propagate naturally — do not handle
        if (exception is OperationCanceledException)
            return false;

        var problem = exception switch
        {
            OmniSiftException known => new ProblemDetails { Status = known.StatusCode, Detail = known.Message },
            BadHttpRequestException badRequest => new ProblemDetails { Status = badRequest.StatusCode, Detail = badRequest.Message },
            HttpRequestException => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Upstream Service Error",
                Detail = "An upstream service request failed."
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred."
            }
        };

        var statusCode = problem.Status!.Value;
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "Request {Method} {Path} failed with {StatusCode}: {Message}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                statusCode,
                exception.Message);
        }

        problem.Instance = httpContext.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;

        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        }).ConfigureAwait(false);

        // No writer accepted the request (e.g. Accept excludes JSON) — still answer.
        if (!written)
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
