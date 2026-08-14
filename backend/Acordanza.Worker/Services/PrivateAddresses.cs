using System.Net;
using System.Net.Sockets;

namespace Acordanza.Worker.Services;

// "May the worker open a TCP connection to this address?" (plan 31 §3).
//
// The worker runs in a Container Apps environment carrying a user-assigned managed
// identity that holds Storage Blob Data Contributor, Storage Queue Data Contributor and
// Cognitive Services User. "Fetch this URL for me", from an authenticated but non-admin
// user, is a request to make an outbound connection from inside that trust boundary — and
// the interesting targets (the instance metadata endpoint, the Postgres host, anything
// else on the environment's network) are all a private IP away.
//
// Pure and static, no I/O, so it is unit-tested exhaustively at every range boundary
// rather than reasoned about. Everything not on the list is allowed: an allowlist of the
// public internet is not writable, so the deny list has to be the complete one.
public static class PrivateAddresses
{
    public static bool IsPublic(IPAddress address)
    {
        // The mapped form is the one that gets missed: ::ffff:169.254.169.254 is the
        // metadata endpoint wearing an IPv6 costume, and every v4 rule below would skip it
        // if it were only checked as v6. Unmap first, then apply the v4 rules.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            // Unix sockets and anything else exotic: not something a URL should reach.
            _ => false
        };
    }

    private static bool IsPublicV4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        uint value = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);

        return !(
            InRange(value, "0.0.0.0", 8) ||          // "this network"
            InRange(value, "10.0.0.0", 8) ||         // RFC 1918
            InRange(value, "100.64.0.0", 10) ||      // CGNAT
            InRange(value, "127.0.0.0", 8) ||        // loopback
            InRange(value, "169.254.0.0", 16) ||     // link-local — the metadata endpoint
            InRange(value, "172.16.0.0", 12) ||      // RFC 1918
            InRange(value, "192.0.0.0", 24) ||       // IETF protocol assignments
            InRange(value, "192.168.0.0", 16) ||     // RFC 1918
            InRange(value, "198.18.0.0", 15) ||      // benchmarking
            InRange(value, "224.0.0.0", 4) ||        // multicast
            InRange(value, "240.0.0.0", 4));         // reserved, incl. 255.255.255.255
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback)) return false;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;

        // fc00::/7 — unique local addresses, the v6 equivalent of RFC 1918. .NET has no
        // property for these (IsIPv6SiteLocal is the deprecated fec0::/10), so it is one
        // masked byte.
        var b = address.GetAddressBytes();
        if ((b[0] & 0xFE) == 0xFC) return false;

        return true;
    }

    private static bool InRange(uint address, string network, int prefixLength)
    {
        var netBytes = IPAddress.Parse(network).GetAddressBytes();
        uint net = (uint)((netBytes[0] << 24) | (netBytes[1] << 16) | (netBytes[2] << 8) | netBytes[3]);
        // A /0 would shift by 32, which is undefined for uint in C# (it shifts by 0), so
        // the mask is built from the complement instead. No caller passes 0 today; this is
        // one line of not having to remember that.
        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        return (address & mask) == (net & mask);
    }
}
