// ============================================================
// Lindrake.Api — Web page fetcher (SSRF-guarded)
// Downloads a user-supplied URL for web ingestion without
// letting it reach internal or cloud-metadata addresses.
// ============================================================

using System.Net;
using System.Net.Sockets;
using Lindrake.Api.Infrastructure;

namespace Lindrake.Api.Services.Ingestion;

/// <summary>Downloads a web page for ingestion.</summary>
public interface IWebPageFetcher
{
    /// <summary>
    /// Downloads <paramref name="url"/> and returns its body.
    /// Throws <see cref="BlockedUrlException"/> when the URL is not http(s) or
    /// resolves to a non-public address, and <see cref="HttpRequestException"/>
    /// when the upstream request fails or the body exceeds the size cap.
    /// </summary>
    Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed-client <see cref="IWebPageFetcher"/>. The SSRF guard lives in the primary
/// handler's <see cref="SocketsHttpHandler.ConnectCallback"/>: every connection —
/// including each redirect hop — resolves DNS itself, rejects the host if any
/// address fails <see cref="OutboundAddressFilter"/>, then connects to exactly the
/// addresses it checked, so DNS rebinding cannot swap in an internal address.
/// </summary>
public sealed class WebPageFetcher(HttpClient httpClient, ILogger<WebPageFetcher> logger) : IWebPageFetcher
{
    /// <summary>Name of the typed HttpClient registration.</summary>
    public const string ClientName = "WebIngestion";

    /// <summary>Per-fetch timeout (whole request, retries included).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BlockedUrlException(url, "only absolute http and https URLs can be ingested");
        }

        try
        {
            // GetAsync buffers the body, enforcing MaxResponseContentBufferSize.
            using var response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (FindBlocked(ex) is { } blocked)
        {
            logger.LogWarning("Blocked web ingestion of {Url}: {Reason}", url, blocked.Message);
            throw new BlockedUrlException(url, blocked.Reason);
        }
    }

    /// <summary>
    /// Primary handler with the SSRF guard. No proxy: through a proxy the guard
    /// would vet the proxy's address, not the target's.
    /// </summary>
    public static SocketsHttpHandler CreatePrimaryHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = ConnectToPublicAddressAsync
    };

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        if (addresses.Length == 0)
            throw new BlockedAddressException("the host did not resolve");

        // Reject the host outright if ANY address is internal, rather than picking
        // the public ones — a mixed answer is itself a rebinding/probing signal.
        // The address itself is deliberately not reported back to the caller.
        if (!addresses.All(OutboundAddressFilter.IsPublic))
            throw new BlockedAddressException("the host resolves to a non-public address");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/> (as surfaced by the handler pipeline)
    /// stems from the SSRF guard. Used to unwrap it and to keep the resilience
    /// handler from retrying or tripping its circuit breaker on blocked URLs.
    /// </summary>
    public static bool IsBlocked(Exception? exception) => FindBlocked(exception) is not null;

    private static BlockedAddressException? FindBlocked(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is BlockedAddressException blocked)
                return blocked;
        }

        return null;
    }

    /// <summary>Raised inside the ConnectCallback; wrapped by SocketsHttpHandler.</summary>
    private sealed class BlockedAddressException(string reason) : IOException(reason)
    {
        public string Reason { get; } = reason;
    }
}
