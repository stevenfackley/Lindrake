// ============================================================
// Lindrake.Api — Outbound address filter (SSRF guard)
// Pure classification of IP addresses a user-supplied URL may
// make the server connect to.
// ============================================================

using System.Net;
using System.Net.Sockets;

namespace Lindrake.Api.Services.Ingestion;

/// <summary>
/// Decides whether the server may open a connection to an address on behalf of
/// a user-supplied URL. Only globally routable unicast addresses pass.
/// </summary>
public static class OutboundAddressFilter
{
    /// <summary>
    /// False for loopback, unspecified, private (10/8, 172.16/12, 192.168/16),
    /// carrier-grade NAT (100.64/10), link-local (169.254/16 — including the cloud
    /// metadata endpoint 169.254.169.254 — and fe80::/10), unique-local (fc00::/7,
    /// which holds the AWS IPv6 metadata endpoint), site-local, multicast and
    /// reserved addresses. IPv4 embedded in IPv6 (mapped, NAT64) is checked as IPv4.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
            return IsPublicIPv4(address.MapToIPv4());

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIPv4(address),
            AddressFamily.InterNetworkV6 => IsPublicIPv6(address),
            _ => false
        };
    }

    private static bool IsPublicIPv4(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        address.TryWriteBytes(b, out _);

        return !(b[0] == 0                                   // 0.0.0.0/8 "this network" (0.0.0.0 reaches localhost)
            || b[0] == 10                                    // 10.0.0.0/8 private
            || (b[0] == 100 && (b[1] & 0xC0) == 64)          // 100.64.0.0/10 carrier-grade NAT
            || b[0] == 127                                   // 127.0.0.0/8 loopback
            || (b[0] == 169 && b[1] == 254)                  // 169.254.0.0/16 link-local + cloud metadata
            || (b[0] == 172 && (b[1] & 0xF0) == 16)          // 172.16.0.0/12 private
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // 192.0.0.0/24 IETF protocol assignments
            || (b[0] == 192 && b[1] == 168)                  // 192.168.0.0/16 private
            || (b[0] == 198 && (b[1] & 0xFE) == 18)          // 198.18.0.0/15 benchmarking
            || b[0] >= 224);                                 // multicast, reserved, broadcast
    }

    private static bool IsPublicIPv6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any)
            || IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal                       // fe80::/10
            || address.IsIPv6SiteLocal                       // fec0::/10 (deprecated)
            || address.IsIPv6UniqueLocal                     // fc00::/7
            || address.IsIPv6Multicast)                      // ff00::/8
        {
            return false;
        }

        Span<byte> b = stackalloc byte[16];
        address.TryWriteBytes(b, out _);

        // 64:ff9b::/96 NAT64 — the last 4 bytes are an IPv4 address.
        if (b[..12].SequenceEqual(Nat64Prefix))
            return IsPublicIPv4(new IPAddress(b[12..]));

        return true;
    }

    private static ReadOnlySpan<byte> Nat64Prefix => [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];
}
