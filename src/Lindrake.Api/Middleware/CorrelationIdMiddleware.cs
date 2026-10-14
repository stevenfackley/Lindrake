// ============================================================
// OmniSift.Api — Correlation ID Middleware
// Reads X-Correlation-Id from request headers (or generates a
// new GUID), pushes it into Serilog's LogContext so every log
// line emitted during the request carries the correlation ID,
// and echoes it back in the response header.
// ============================================================

using Serilog.Context;

namespace OmniSift.Api.Middleware;

/// <summary>
/// Ensures every request has a correlation ID available in both
/// the response header and the Serilog log context.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        // Reuse caller-supplied ID or mint a new one.
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString();

        // Store for downstream code that wants it directly.
        context.Items[HeaderName] = correlationId;

        // Echo back so clients can correlate their own traces.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Push into Serilog LogContext — all log events on this
        // thread/async flow will carry CorrelationId automatically.
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}

/// <summary>Extension method for terse pipeline registration.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        => builder.UseMiddleware<CorrelationIdMiddleware>();
}
