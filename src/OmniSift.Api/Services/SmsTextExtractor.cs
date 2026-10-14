// ============================================================
// OmniSift.Api — SMS Text Extractor
// Parses SMS exports from CSV or JSON formats
// ============================================================

using System.Text;
using System.Text.Json;

namespace OmniSift.Api.Services;

/// <summary>
/// Extracts text from SMS exports in CSV or JSON format.
/// CSV format: sender,timestamp,message (with header row)
/// JSON format: Array of { sender, timestamp, message } objects
/// </summary>
public sealed class SmsTextExtractor(ILogger<SmsTextExtractor> logger) : ITextExtractor
{
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
                var sender = GetJsonString(element, "sender", "from", "name", "phone");
                var timestamp = GetJsonString(element, "timestamp", "date", "time", "datetime");
                var message = GetJsonString(element, "message", "body", "text", "content");

                if (!string.IsNullOrWhiteSpace(message))
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(sender)) parts.Add(sender);
                    if (!string.IsNullOrWhiteSpace(timestamp)) parts.Add($"({timestamp})");
                    parts.Add(message);
                    messages.Add(string.Join(" ", parts));
                }
            }

            logger.LogInformation(
                "Parsed {Count} SMS messages from JSON: {FileName}",
                messages.Count, fileName ?? "unknown");

            return messages.Count > 0
                ? "[SMS Conversation]\n" + string.Join("\n", messages)
                : string.Empty;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse SMS JSON: {FileName}", fileName ?? "unknown");
            throw new InvalidOperationException($"Failed to parse SMS JSON file: {ex.Message}", ex);
        }
    }

    private string ParseCsv(string content, string? fileName)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length <= 1)
        {
            logger.LogWarning("CSV has no data rows: {FileName}", fileName ?? "unknown");
            return string.Empty;
        }

        // Parse header to find column indices
        var header = ParseCsvLine(lines[0]);
        var senderIdx = FindColumnIndex(header, "sender", "from", "name", "phone");
        var timestampIdx = FindColumnIndex(header, "timestamp", "date", "time", "datetime");
        var messageIdx = FindColumnIndex(header, "message", "body", "text", "content");

        if (messageIdx < 0)
        {
            // If no message column found, try positional: assume sender, timestamp, message
            senderIdx = header.Length > 0 ? 0 : -1;
            timestampIdx = header.Length > 1 ? 1 : -1;
            messageIdx = header.Length > 2 ? 2 : (header.Length > 0 ? header.Length - 1 : -1);
        }

        var messages = new List<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            var fields = ParseCsvLine(lines[i]);
            if (fields.Length == 0) continue;

            var sender = senderIdx >= 0 && senderIdx < fields.Length ? fields[senderIdx].Trim() : null;
            var timestamp = timestampIdx >= 0 && timestampIdx < fields.Length ? fields[timestampIdx].Trim() : null;
            var message = messageIdx >= 0 && messageIdx < fields.Length ? fields[messageIdx].Trim() : null;

            if (!string.IsNullOrWhiteSpace(message))
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(sender)) parts.Add(sender);
                if (!string.IsNullOrWhiteSpace(timestamp)) parts.Add($"({timestamp})");
                parts.Add(message);
                messages.Add(string.Join(" ", parts));
            }
        }

        logger.LogInformation(
            "Parsed {Count} SMS messages from CSV: {FileName}",
            messages.Count, fileName ?? "unknown");

        return messages.Count > 0
            ? "[SMS Conversation]\n" + string.Join("\n", messages)
            : string.Empty;
    }

    /// <summary>
    /// Simple CSV line parser that handles quoted fields.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // Skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static int FindColumnIndex(string[] header, params string[] candidates)
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

    private static string? GetJsonString(JsonElement element, params string[] propertyNames)
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
