using System.Text.Json;
using OmniSift.Shared;
using OmniSift.Shared.DTOs;

namespace OmniSift.Web.Services;

/// <summary>
/// Pure SSE frame parser: converts raw "data: {json}\n\n" lines into typed events.
/// Extracted for unit-testability — no I/O here.
/// </summary>
public static class SseParser
{
    /// <summary>
    /// Parse a single raw SSE line (e.g. "data: {...}") into a typed event.
    /// Returns null if the line is not a data frame or cannot be parsed.
    /// </summary>
    public static SseEvent? ParseLine(string line)
    {
        if (!line.StartsWith("data: ", StringComparison.Ordinal))
            return null;

        var json = line["data: ".Length..].Trim();
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            var type = typeProp.GetString();

            if (type == "delta")
            {
                var content = root.TryGetProperty("content", out var cp) ? cp.GetString() ?? "" : "";
                return new SseEvent.Delta(content);
            }

            if (type == "final")
            {
                var final = JsonSerializer.Deserialize(json, OmniSiftJsonContext.Default.AgentStreamFinalEvent);
                return final is null ? null : new SseEvent.Final(final);
            }

            if (type == "error")
            {
                var message = root.TryGetProperty("message", out var mp) ? mp.GetString() : null;
                return new SseEvent.Error(string.IsNullOrWhiteSpace(message) ? SseEvent.Error.DefaultMessage : message);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Discriminated union for SSE events (no external deps — safe to test).
/// </summary>
public abstract record SseEvent
{
    /// <summary>A chunk of the answer text.</summary>
    public sealed record Delta(string Content) : SseEvent;

    /// <summary>The answer is complete; carries citations, plugins and timing.</summary>
    public sealed record Final(AgentStreamFinalEvent Data) : SseEvent;

    /// <summary>The run failed: the server's terminal <c>error</c> frame, or a failed request.</summary>
    public sealed record Error(string Message) : SseEvent
    {
        public const string DefaultMessage = "The research agent could not finish this response. Please try again.";
    }
}
