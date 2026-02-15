using System.Net;
using System.Net.Sockets;

namespace Talos.Web.Services;

/// <summary>
/// SSRF (Server-Side Request Forgery) protection for outbound HTTP requests.
/// Prevents fetching URLs that resolve to private, reserved, or loopback IP addresses.
/// Covers GAP-12 (DISC-7) per IndieAuth spec §4.2.
/// </summary>
public static class SsrfProtection
{
    /// <summary>
    /// Checks whether an IP address is private, reserved, or loopback.
    /// These addresses MUST NOT be contacted when fetching user-provided URLs.
    /// </summary>
    public static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        // IPv4-mapped IPv6 addresses: check the underlying IPv4 address
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivateOrReservedAddress(address.MapToIPv4());
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 0.0.0.0/8 — "This" network
            if (bytes[0] == 0) return true;

            // 10.0.0.0/8 — Private
            if (bytes[0] == 10) return true;

            // 100.64.0.0/10 — Carrier-grade NAT (shared address space)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;

            // 127.0.0.0/8 — Loopback
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 — Link-local / cloud metadata (169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 — Private
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.0.0.0/24 — IETF Protocol Assignments
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return true;

            // 192.0.2.0/24 — TEST-NET-1 (documentation)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return true;

            // 192.168.0.0/16 — Private
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 198.18.0.0/15 — Benchmark testing
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) return true;

            // 198.51.100.0/24 — TEST-NET-2 (documentation)
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;

            // 203.0.113.0/24 — TEST-NET-3 (documentation)
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;

            // 224.0.0.0/4 — Multicast
            if (bytes[0] >= 224 && bytes[0] <= 239) return true;

            // 240.0.0.0/4 — Reserved for future use
            if (bytes[0] >= 240) return true;

            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 — Loopback
            if (IPAddress.IsLoopback(address)) return true;

            // :: — Unspecified
            if (address.Equals(IPAddress.IPv6None)) return true;

            // fe80::/10 — Link-local
            if (address.IsIPv6LinkLocal) return true;

            // fc00::/7 — Unique local addresses (ULA, equivalent of private IPv4)
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            // ff00::/8 — Multicast
            if (bytes[0] == 0xFF) return true;

            return false;
        }

        // Unknown address family — block by default
        return true;
    }

    /// <summary>
    /// Creates a <see cref="SocketsHttpHandler"/> whose ConnectCallback resolves DNS
    /// and blocks connections to private/reserved IP addresses.
    /// This provides transport-level SSRF protection that also catches redirects.
    /// </summary>
    public static SocketsHttpHandler CreateSsrfSafeHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectCallback = SsrfSafeConnectAsync
        };
    }

    private static async ValueTask<Stream> SsrfSafeConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        // Resolve DNS
        var entry = await Dns.GetHostEntryAsync(host, cancellationToken);

        if (entry.AddressList.Length == 0)
        {
            throw new HttpRequestException($"DNS resolution for '{host}' returned no addresses");
        }

        // Filter to safe addresses only
        var safeAddresses = entry.AddressList
            .Where(a => !IsPrivateOrReservedAddress(a))
            .ToArray();

        if (safeAddresses.Length == 0)
        {
            throw new HttpRequestException(
                $"SSRF protection: all resolved addresses for '{host}' are private or reserved");
        }

        // Connect to the first available safe address
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.NoDelay = true;
            await socket.ConnectAsync(safeAddresses, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
