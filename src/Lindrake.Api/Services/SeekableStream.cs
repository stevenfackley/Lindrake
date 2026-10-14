// ============================================================
// OmniSift.Api — Seekable input for extractors that rewind
// ============================================================

namespace OmniSift.Api.Services;

internal static class SeekableStream
{
    /// <summary>
    /// Null when <paramref name="stream"/> can already seek (use it directly);
    /// otherwise a rewound in-memory copy the caller must dispose. The ingestion
    /// pipeline always passes a MemoryStream, so the copy is the rare case.
    /// </summary>
    public static async Task<MemoryStream?> BufferIfNeededAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            return null;

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }
}
