// ============================================================
// Lindrake.Web — RFC 7807 ProblemDetails → user-facing message
// ============================================================

using System.Net;
using System.Text.Json;

namespace Lindrake.Web.Services;

/// <summary>
/// Reads an error response (the API answers every failure with RFC 7807
/// ProblemDetails) into an <see cref="ApiError"/>. Uses <see cref="JsonDocument"/>,
/// so no reflection-based serialization is involved.
/// </summary>
public static class ApiProblemReader
{
    private const int MaxValidationMessages = 5;

    /// <summary>Reads the error body of a non-success <paramref name="response"/>.</summary>
    public static async Task<ApiError> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return FromBody((int)response.StatusCode, body);
    }

    /// <summary>
    /// The message for an error response: validation <c>errors</c> when present,
    /// else <c>detail</c>, else <c>title</c>, else a generic text for the status.
    /// A body that is not a JSON object (e.g. a proxy's HTML error page) is ignored.
    /// </summary>
    public static ApiError FromBody(int statusCode, string? body)
    {
        string? title = null;
        string? detail = null;
        var validation = new List<string>();

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    title = ReadString(root, "title");
                    detail = ReadString(root, "detail");
                    ReadValidationErrors(root, validation);
                }
            }
            catch (JsonException)
            {
                // Not JSON: fall back to the status text below.
            }
        }

        var message = validation.Count > 0 ? string.Join(" ", validation)
            : !string.IsNullOrWhiteSpace(detail) ? detail
            : !string.IsNullOrWhiteSpace(title) ? title
            : DefaultMessage(statusCode);

        return new ApiError(statusCode, message);
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ValidationProblemDetails: "errors": { "Field": ["message", ...], ... }
    private static void ReadValidationErrors(JsonElement root, List<string> messages)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
            return;

        foreach (var field in errors.EnumerateObject())
        {
            if (field.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in field.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                    messages.Add(text);

                if (messages.Count == MaxValidationMessages)
                    return;
            }
        }
    }

    private static string DefaultMessage(int statusCode) => statusCode switch
    {
        (int)HttpStatusCode.Unauthorized => "You are not signed in, or your session has expired.",
        (int)HttpStatusCode.Forbidden => "You do not have access to this resource.",
        (int)HttpStatusCode.NotFound => "The requested item was not found.",
        (int)HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
        >= 500 => $"The server could not complete the request (HTTP {statusCode}).",
        _ => $"The request failed (HTTP {statusCode})."
    };
}
