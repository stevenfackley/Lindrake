// ============================================================
// OmniSift.Api — Text Extractor Interface
// Strategy pattern for extracting text from different formats
// ============================================================

namespace OmniSift.Api.Services;

/// <summary>
/// Defines the contract for extracting raw text from a data source.
/// Implementations handle specific formats (PDF, SMS, HTML).
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// The source type this extractor handles (e.g., "pdf", "sms", "web").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Extracts raw text from the provided stream.
    /// </summary>
    /// <param name="stream">The input data stream.</param>
    /// <param name="fileName">Original file name (for format detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted text content.</returns>
    Task<string> ExtractTextAsync(Stream stream, string? fileName = null, CancellationToken cancellationToken = default);
}
