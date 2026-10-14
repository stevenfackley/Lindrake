// ============================================================
// Lindrake.Web — Server-Sent Events parser
// Event-stream interpretation per the WHATWG HTML spec (§9.2.6),
// for the agent stream written by the API's AgentSseWriter.
// ============================================================

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lindrake.Shared;
using Lindrake.Shared.DTOs;

namespace Lindrake.Web.Services;

/// <summary>
/// Assembles SSE lines into events: <c>data:</c> fields (with or without the
/// space after the colon) accumulate, several joined with <c>\n</c>, and a blank
/// line dispatches the event. Comment lines (<c>:</c>) and the <c>event</c>,
/// <c>id</c> and <c>retry</c> fields are ignored — the event kind is the JSON
/// payload's <c>type</c>. Line splitting (<c>\r\n</c>, <c>\n</c>, <c>\r</c>) is
/// left to <see cref="TextReader"/>, which handles all three.
/// </summary>
public sealed class SseParser
{
    private readonly StringBuilder _data = new();
    private bool _hasData;

    /// <summary>
    /// Feeds one line (without its terminator). Returns the event a blank line
    /// dispatches, or null (no event yet, or one this client does not know).
    /// </summary>
    public SseEvent? PushLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Length == 0)
            return Dispatch();

        if (line[0] == ':')
            return null; // comment, e.g. a keep-alive

        var colon = line.IndexOf(':', StringComparison.Ordinal);
        var field = colon < 0 ? line.AsSpan() : line.AsSpan(0, colon);
        var value = colon < 0 ? [] : line.AsSpan(colon + 1);
        if (value.Length > 0 && value[0] == ' ')
            value = value[1..];

        if (field.SequenceEqual("data"))
        {
            _data.Append(value).Append('\n');
            _hasData = true;
        }

        return null;
    }

    /// <summary>
    /// Reads events until the end of <paramref name="reader"/>. As the spec requires,
    /// a final event not followed by a blank line is discarded.
    /// </summary>
    public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var parser = new SseParser();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (parser.PushLine(line) is { } evt)
                yield return evt;
        }
    }

    /// <summary>Parses a complete event-stream text.</summary>
    public static IReadOnlyList<SseEvent> ParseAll(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parser = new SseParser();
        var events = new List<SseEvent>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (parser.PushLine(line) is { } evt)
                events.Add(evt);
        }

        return events;
    }

    /// <summary>
    /// Converts one event's data (a JSON object with a <c>type</c>) into a typed event.
    /// Null for unknown types and malformed JSON.
    /// </summary>
    public static SseEvent? ParseData(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;

        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            switch (type.GetString())
            {
                case "delta":
                    return new SseEvent.Delta(ReadString(root, "content") ?? string.Empty);

                case "final":
                    return root.Deserialize(LindrakeJsonContext.Default.AgentStreamFinalEvent) is { } final
                        ? new SseEvent.Final(final)
                        : null;

                case "error":
                    return new SseEvent.Error(ReadString(root, "message") is { Length: > 0 } message
                        ? message
                        : SseEvent.Error.DefaultMessage);

                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SseEvent? Dispatch()
    {
        if (!_hasData)
            return null;

        // The data buffer ends with the LF appended after its last line.
        var data = _data.ToString(0, _data.Length - 1);
        _data.Clear();
        _hasData = false;
        return ParseData(data);
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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
