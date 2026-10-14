// ============================================================
// Lindrake.Api — Server-Sent Events writer for streamed agent runs
// Wire format consumed by Lindrake.Web's SseParser:
//   data: {"type":"delta","content":"..."}\n\n
//   data: {"type":"final","pluginsUsed":[...],"durationMs":n,"sources":[...]}\n\n
//   data: {"type":"error","message":"..."}\n\n   (only if the stream fails mid-way)
// ============================================================

using System.Text.Json;
using Lindrake.Shared;
using Lindrake.Shared.DTOs;

namespace Lindrake.Api.Services.Agent;

/// <summary>Writes an agent event stream to an HTTP response as SSE frames.</summary>
public sealed class AgentSseWriter(ILogger<AgentSseWriter> logger)
{
    /// <summary>Client-facing text of the terminal error frame. Never the exception message.</summary>
    public const string StreamFailedMessage =
        "The research agent could not finish this response. Please try again.";

    /// <summary>
    /// Streams <paramref name="events"/> as SSE frames, flushing each one. A failure
    /// before the first frame propagates (the response is still uncommitted, so the
    /// exception handler can answer with ProblemDetails); a failure after it is logged
    /// and reported with a terminal <c>error</c> frame instead of a silently truncated stream.
    /// </summary>
    public async Task WriteAsync(
        HttpResponse response,
        IAsyncEnumerable<AgentStreamEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(events);

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                await WriteFrameAsync(response, Format(evt), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (response.HasStarted && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Agent stream failed after the response started");
            await WriteFrameAsync(response, ErrorFrame(StreamFailedMessage), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The SSE frame for one agent event.</summary>
    public static string Format(AgentStreamEvent evt) => evt switch
    {
        AgentStreamEvent.Delta delta => Frame(JsonSerializer.Serialize(
            new AgentStreamDeltaEvent { Type = "delta", Content = delta.Content },
            LindrakeJsonContext.Default.AgentStreamDeltaEvent)),
        AgentStreamEvent.Completed completed => Frame(JsonSerializer.Serialize(
            completed.Result.ToFinalEvent(),
            LindrakeJsonContext.Default.AgentStreamFinalEvent)),
        _ => throw new ArgumentOutOfRangeException(nameof(evt), evt, "Unknown agent stream event.")
    };

    /// <summary>The terminal SSE error frame.</summary>
    public static string ErrorFrame(string message) => Frame(JsonSerializer.Serialize(
        new AgentStreamErrorEvent { Type = "error", Message = message },
        LindrakeJsonContext.Default.AgentStreamErrorEvent));

    private static string Frame(string json) => $"data: {json}\n\n";

    private static async Task WriteFrameAsync(HttpResponse response, string frame, CancellationToken cancellationToken)
    {
        await response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
