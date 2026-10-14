// ============================================================
// OmniSift.Api — Domain exceptions with an HTTP status
// GlobalExceptionHandler surfaces StatusCode + Message for these;
// every other exception becomes a generic 500.
// ============================================================

namespace OmniSift.Api.Infrastructure;

/// <summary>
/// Base for expected, client-facing failures. <see cref="Exception.Message"/>
/// is returned to the caller verbatim, so it must not contain internals.
/// </summary>
public abstract class OmniSiftException : Exception
{
    protected OmniSiftException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code reported to the client.</summary>
    public int StatusCode { get; }
}

/// <summary>An ingestion source type has no registered text extractor (400).</summary>
public sealed class UnsupportedSourceTypeException(string sourceType, IEnumerable<string> allowed)
    : OmniSiftException(
        StatusCodes.Status400BadRequest,
        $"Unsupported source type: '{sourceType}'. Allowed: {string.Join(", ", allowed)}")
{
    public string SourceType { get; } = sourceType;
}

/// <summary>The request carries no resolved tenant (403).</summary>
public sealed class TenantContextMissingException()
    : OmniSiftException(StatusCodes.Status403Forbidden, "No tenant context is available for this request.");

/// <summary>No chat-completion service is registered on the kernel (503).</summary>
public sealed class LlmNotConfiguredException()
    : OmniSiftException(
        StatusCodes.Status503ServiceUnavailable,
        "LLM service not configured. Set Anthropic:ApiKey in configuration.");
