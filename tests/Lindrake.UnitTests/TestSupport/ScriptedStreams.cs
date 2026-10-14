// ============================================================
// Unit Tests — response bodies that behave like a live event stream
// ============================================================

using System.Net;
using System.Text;

namespace Lindrake.UnitTests.TestSupport;

/// <summary>
/// A read-only stream that returns <paramref name="prefix"/> and then either hangs
/// until the read is cancelled, or throws <paramref name="failWith"/>.
/// </summary>
internal sealed class ScriptedReadStream(byte[] prefix, Exception? failWith = null) : Stream
{
    private int _position;

    public static HttpResponseMessage Response(string body, Exception? failWith = null) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new ScriptedReadStream(Encoding.UTF8.GetBytes(body), failWith)) };

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        if (failWith is not null)
            throw failWith;

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>String content that records whether it was disposed.</summary>
internal sealed class TrackingContent(string body) : StringContent(body, Encoding.UTF8, "text/event-stream")
{
    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
