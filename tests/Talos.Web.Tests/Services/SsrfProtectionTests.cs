using System.Net;
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for SsrfProtection.IsPrivateOrReservedAddress covering GAP-12 (DISC-7).
/// Validates that private, reserved, loopback, link-local, cloud metadata,
/// and other non-routable addresses are correctly identified and blocked.
/// </summary>
public class SsrfProtectionTests
{
    // ===== IPv4 private ranges =====

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.100")]
    public void IsPrivateOrReservedAddress_IPv4PrivateRanges_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is a private IPv4 address");
    }

    // ===== IPv4 loopback =====

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("127.255.255.255")]
    public void IsPrivateOrReservedAddress_IPv4Loopback_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is a loopback address");
    }

    // ===== Link-local / cloud metadata (169.254.x.x) =====

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]  // AWS/GCP/Azure metadata endpoint
    [InlineData("169.254.255.255")]
    public void IsPrivateOrReservedAddress_LinkLocal_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is a link-local / cloud metadata address");
    }

    // ===== Carrier-grade NAT (100.64.0.0/10) =====

    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    public void IsPrivateOrReservedAddress_CarrierGradeNat_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is carrier-grade NAT");
    }

    // ===== "This" network (0.0.0.0/8) =====

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    public void IsPrivateOrReservedAddress_ThisNetwork_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is in the 'this' network range");
    }

    // ===== Documentation / test ranges =====

    [Theory]
    [InlineData("192.0.2.1")]      // TEST-NET-1
    [InlineData("198.51.100.1")]    // TEST-NET-2
    [InlineData("203.0.113.1")]     // TEST-NET-3
    public void IsPrivateOrReservedAddress_DocumentationRanges_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is a documentation/test range");
    }

    // ===== Benchmark testing (198.18.0.0/15) =====

    [Theory]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    public void IsPrivateOrReservedAddress_BenchmarkRange_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is in the benchmark testing range");
    }

    // ===== Multicast & reserved =====

    [Theory]
    [InlineData("224.0.0.1")]     // Multicast
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]     // Reserved
    [InlineData("255.255.255.255")]
    public void IsPrivateOrReservedAddress_MulticastAndReserved_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is multicast or reserved");
    }

    // ===== IETF Protocol Assignments (192.0.0.0/24) =====

    [Fact]
    public void IsPrivateOrReservedAddress_IetfProtocolAssignments_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse("192.0.0.1"))
            .Should().BeTrue();
    }

    // ===== IPv6 loopback =====

    [Fact]
    public void IsPrivateOrReservedAddress_IPv6Loopback_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.IPv6Loopback)
            .Should().BeTrue("::1 is the IPv6 loopback address");
    }

    // ===== IPv6 unspecified =====

    [Fact]
    public void IsPrivateOrReservedAddress_IPv6Unspecified_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.IPv6None)
            .Should().BeTrue(":: is the unspecified address");
    }

    // ===== IPv6 link-local =====

    [Fact]
    public void IsPrivateOrReservedAddress_IPv6LinkLocal_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse("fe80::1"))
            .Should().BeTrue("fe80::/10 is link-local");
    }

    // ===== IPv6 unique local (ULA) =====

    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    public void IsPrivateOrReservedAddress_IPv6UniqueLocal_ReturnsTrue(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeTrue($"{ip} is a unique local address (IPv6 private)");
    }

    // ===== IPv6 multicast =====

    [Fact]
    public void IsPrivateOrReservedAddress_IPv6Multicast_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse("ff02::1"))
            .Should().BeTrue("ff00::/8 is multicast");
    }

    // ===== IPv4-mapped IPv6 =====

    [Fact]
    public void IsPrivateOrReservedAddress_IPv4MappedIPv6Private_ReturnsTrue()
    {
        // ::ffff:10.0.0.1 is an IPv4-mapped IPv6 address for 10.0.0.1
        var mapped = IPAddress.Parse("10.0.0.1").MapToIPv6();
        SsrfProtection.IsPrivateOrReservedAddress(mapped)
            .Should().BeTrue("IPv4-mapped IPv6 of a private address should be blocked");
    }

    [Fact]
    public void IsPrivateOrReservedAddress_IPv4MappedIPv6CloudMetadata_ReturnsTrue()
    {
        // ::ffff:169.254.169.254
        var mapped = IPAddress.Parse("169.254.169.254").MapToIPv6();
        SsrfProtection.IsPrivateOrReservedAddress(mapped)
            .Should().BeTrue("IPv4-mapped IPv6 of cloud metadata address should be blocked");
    }

    [Fact]
    public void IsPrivateOrReservedAddress_IPv4MappedIPv6Public_ReturnsFalse()
    {
        var mapped = IPAddress.Parse("8.8.8.8").MapToIPv6();
        SsrfProtection.IsPrivateOrReservedAddress(mapped)
            .Should().BeFalse("IPv4-mapped IPv6 of a public address should not be blocked");
    }

    // ===== Public addresses (should NOT be blocked) =====

    [Theory]
    [InlineData("8.8.8.8")]            // Google DNS
    [InlineData("1.1.1.1")]            // Cloudflare DNS
    [InlineData("93.184.216.34")]       // example.com
    [InlineData("151.101.1.140")]       // reddit.com
    [InlineData("172.15.255.255")]      // Just outside 172.16.0.0/12
    [InlineData("172.32.0.1")]          // Just outside 172.16.0.0/12
    [InlineData("100.63.255.255")]      // Just outside 100.64.0.0/10
    [InlineData("100.128.0.1")]         // Just outside 100.64.0.0/10
    [InlineData("192.169.0.1")]         // Just outside 192.168.0.0/16
    public void IsPrivateOrReservedAddress_PublicIPv4_ReturnsFalse(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeFalse($"{ip} is a public IPv4 address");
    }

    [Theory]
    [InlineData("2001:4860:4860::8888")]   // Google DNS IPv6
    [InlineData("2606:4700:4700::1111")]   // Cloudflare DNS IPv6
    public void IsPrivateOrReservedAddress_PublicIPv6_ReturnsFalse(string ip)
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse(ip))
            .Should().BeFalse($"{ip} is a public IPv6 address");
    }

    // ===== Boundary tests for edge cases =====

    [Fact]
    public void IsPrivateOrReservedAddress_172_16_LowerBound_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse("172.16.0.0"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsPrivateOrReservedAddress_172_31_UpperBound_ReturnsTrue()
    {
        SsrfProtection.IsPrivateOrReservedAddress(IPAddress.Parse("172.31.255.255"))
            .Should().BeTrue();
    }
}
