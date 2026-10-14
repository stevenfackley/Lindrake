// ============================================================
// Lindrake.Api — SMS Text Extractor
// Parses SMS exports from CSV or JSON formats
// ============================================================

using System.Text;
using System.Text.Json;

namespace Lindrake.Api.Services;

/// <summary>
/// Extracts text from SMS exports in CSV or JSON format.
/// CSV format: sender,timestamp,message (with header row)
/// JSON format: Array of { sender, timestamp, message } objects
/// Both produce "[SMS Conversation]" followed by one "sender (timestamp) message"
/// line per message.
/// </summary>
public sealed class SmsTextExtractor(ILogger<SmsTextExtractor> logger) : ITextExtractor
{
    private static readonly string[] SenderColumns = ["sender", "from", "name", "phone"];
    private static readonly string[] TimestampColumns = ["timestamp", "date", "time", "datetime"];
    private static readonly string[] MessageColumns = ["message", "body", "text", "content"];

    /// <inheritdoc />
    public string SourceType => SourceTypes.Sms;

    /// <inheritdoc />
    public async Task<string> ExtractTextAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        logger.LogDebug("Extracting text from SMS export: {FileName}", fileName ?? "unknown");

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty SMS export: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        var trimmed = content.TrimStart();

        // Detect format: JSON starts with '[' or '{', CSV otherwise
        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            return ParseJson(trimmed, fileName);
        }

        return ParseCsv(trimmed, fileName);
    }

    private string ParseJson(string content, string? fileName)
    {
        try
        {
            // Wrap single object in array
            if (content.TrimStart().StartsWith('{'))
            {
                content = $"[{content}]";
            }

            using var doc = JsonDocument.Parse(content);
            var messages = new List<string>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var line = FormatMessage(
                    GetJsonString(element, SenderColumns),
                    GetJsonString(element, TimestampColumns),
                    GetJsonString(element, MessageColumns));

                if (line is not null)
                    messages.Add(line);
            }

            logger.LogInformation(
                "Parsed {Count} SMS messages from JSON: {FileName}",
                messages.Count, fileName ?? "unknown");

            return FormatConversation(messages);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse SMS JSON: {FileName}", fileName ?? "unknown");
            throw new InvalidOperationException($"Failed to parse SMS JSON file: {ex.Message}", ex);
        }
    }

    private string ParseCsv(string content, string? fileName)
    {
        var records = ReadCsvRecords(content).ToList();

        if (records.Count <= 1)
        {
            logger.LogWarning("CSV has no data rows: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        // Parse header to find column indices
        var header = records[0];
        var senderIdx = FindColumnIndex(header, SenderColumns);
        var timestampIdx = FindColumnIndex(header, TimestampColumns);
        var messageIdx = FindColumnIndex(header, MessageColumns);

        if (messageIdx < 0)
        {
            // If no message column found, try positional: assume sender, timestamp, message
            senderIdx = header.Length > 0 ? 0 : -1;
            timestampIdx = header.Length > 1 ? 1 : -1;
            messageIdx = header.Length > 2 ? 2 : (header.Length > 0 ? header.Length - 1 : -1);
        }

        var messages = new List<string>();

        foreach (var fields in records.Skip(1))
        {
            var line = FormatMessage(
                Field(fields, senderIdx),
                Field(fields, timestampIdx),
                Field(fields, messageIdx));

            if (line is not null)
                messages.Add(line);
        }

        logger.LogInformation(
            "Parsed {Count} SMS messages from CSV: {FileName}",
            messages.Count, fileName ?? "unknown");

        return FormatConversation(messages);
    }

    private static string? Field(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : null;

    /// <summary>
    /// "sender (timestamp) message", omitting whichever of sender/timestamp is
    /// missing. Null when there is no message text.
    /// </summary>
    private static string? FormatMessage(string? sender, string? timestamp, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(sender)) parts.Add(sender);
        if (!string.IsNullOrWhiteSpace(timestamp)) parts.Add($"({timestamp})");
        parts.Add(message);
        return string.Join(' ', parts);
    }

    private static string FormatConversation(List<string> messages) =>
        messages.Count > 0
            ? "[SMS Conversation]\n" + string.Join('\n', messages)
            : string.Empty;

    /// <summary>
    /// RFC 4180-style record reader: commas separate fields; CRLF, LF or CR end a
    /// record; a double-quoted field may contain commas, line breaks and escaped
    /// quotes (""). Blank lines are skipped.
    /// </summary>
    private static IEnumerable<string[]> ReadCsvRecords(string content)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var recordStarted = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < content.Length && content[i + 1] == '"')
                    field.Append(content[++i]); // escaped quote
                else
                    inQuotes = false;

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    recordStarted = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    recordStarted = true;
                    break;

                case '\r' or '\n':
                    if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                        i++;

                    if (recordStarted)
                    {
                        fields.Add(field.ToString());
                        yield return [.. fields];
                    }

                    fields.Clear();
                    field.Clear();
                    recordStarted = false;
                    break;

                default:
                    field.Append(c);
                    recordStarted = true;
                    break;
            }
        }

        if (recordStarted)
        {
            fields.Add(field.ToString());
            yield return [.. fields];
        }
    }

    private static int FindColumnIndex(string[] header, string[] candidates)
    {
        for (var i = 0; i < header.Length; i++)
        {
            var normalized = header[i].Trim().ToLowerInvariant().Replace("\"", "");
            foreach (var candidate in candidates)
            {
                if (normalized == candidate || normalized.Contains(candidate))
                    return i;
            }
        }
        return -1;
    }

    private static string? GetJsonString(JsonElement element, string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }
        return null;
    }
}
