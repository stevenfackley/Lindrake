// ============================================================
// OmniSift.Api — Ingestion Channel
// Bounded System.Threading.Channels queue that decouples
// the upload endpoint from the ingestion pipeline.
// ============================================================

using System.Threading.Channels;

namespace OmniSift.Api.Services;

/// <summary>
/// Wraps a bounded Channel of <see cref="IngestionWorkItem"/>.
/// Registered as a singleton so both the controller (writer)
/// and the background service (reader) share the same instance.
/// </summary>
public sealed class IngestionChannel
{
    private readonly Channel<IngestionWorkItem> _channel =
        Channel.CreateBounded<IngestionWorkItem>(new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>
    /// Write side — used by the controller to enqueue uploads.
    /// </summary>
    public ChannelWriter<IngestionWorkItem> Writer => _channel.Writer;

    /// <summary>
    /// Read side — consumed by <see cref="IngestionBackgroundService"/>.
    /// </summary>
    public ChannelReader<IngestionWorkItem> Reader => _channel.Reader;
}
