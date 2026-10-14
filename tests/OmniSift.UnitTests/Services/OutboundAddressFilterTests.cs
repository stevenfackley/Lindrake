// ============================================================
// Unit Tests — OutboundAddressFilter (SSRF address classifier)
// ============================================================

using System.Net;
using FluentAssertions;
using OmniSift.Api.Services.Ingestion;

namespace OmniSift.UnitTests.Services;

public sealed class OutboundAddressFilterTests
{
    [Theory]
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("127.255.255.254")]
    [InlineData("0.0.0.0")]            // "this host" — reaches localhost
    [InlineData("10.0.0.1")]           // private
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]    // cloud metadata
    [InlineData("169.254.0.1")]        // link-local
    [InlineData("100.64.0.1")]         // carrier-grade NAT
    [InlineData("100.100.100.200")]    // CGNAT-hosted metadata (Alibaba)
    [InlineData("192.0.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("255.255.255.255")]    // broadcast
    [InlineData("::1")]                // IPv6 loopback
    [InlineData("::")]                 // unspecified
    [InlineData("fe80::1")]            // link-local
    [InlineData("fc00::1")]            // unique-local
    [InlineData("fd00:ec2::254")]      // AWS IPv6 metadata
    [InlineData("fec0::1")]            // site-local
    [InlineData("ff02::1")]            // multicast
    [InlineData("::ffff:127.0.0.1")]   // IPv4-mapped loopback
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64-embedded 169.254.169.254
    public void IsPublic_InternalAddress_ReturnsFalse(string address)
    {
        OutboundAddressFilter.IsPublic(IPAddress.Parse(address)).Should().BeFalse();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]     // just below 172.16/12
    [InlineData("172.32.0.1")]         // just above 172.16/12
    [InlineData("100.128.0.1")]        // just above 100.64/10
    [InlineData("169.253.255.255")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]   // NAT64-embedded 8.8.8.8
    public void IsPublic_PublicAddress_ReturnsTrue(string address)
    {
        OutboundAddressFilter.IsPublic(IPAddress.Parse(address)).Should().BeTrue();
    }
}
