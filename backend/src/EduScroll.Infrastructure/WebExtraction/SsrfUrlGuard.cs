using System.Net;
using System.Net.Sockets;
using EduScroll.Application.Exceptions;

namespace EduScroll.Infrastructure.WebExtraction;

/// <summary>
/// Enforces SSRF URL, hostname, and resolved IP address restrictions against
/// loopback, private, link-local, metadata, multicast, and reserved networks.
/// </summary>
public static class SsrfUrlGuard
{
    private static readonly string[] ForbiddenHostSuffixes =
    [
        ".localhost",
        ".local",
        ".internal",
        ".localdomain",
        ".home.arpa",
        ".arpa"
    ];

    private static readonly HashSet<string> ForbiddenExactHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "ip6-localhost",
        "ip6-loopback",
        "metadata",
        "metadata.google.internal",
        "metadata.goog",
        "instance-data"
    };

    public static void ValidateUriSyntaxAndHost(Uri uri, bool isRedirect = false)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            throw CreateBlockedException(isRedirect, "The URL must be an absolute HTTP or HTTPS address.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            if (isRedirect)
            {
                throw CreateBlockedException(true, "The web page redirected to an unsupported URL scheme.");
            }

            throw new ValidationAppException(
                "Only HTTP and HTTPS URLs are supported.",
                new Dictionary<string, string[]> { ["url"] = ["Only http:// and https:// URLs are allowed."] },
                "URL_INVALID_SCHEME");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw CreateBlockedException(isRedirect, "URLs containing embedded credentials are not allowed.");
        }

        var normalizedHost = NormalizeHost(uri.DnsSafeHost);
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            throw CreateBlockedException(isRedirect, "The URL must specify a valid public host.");
        }

        if (ForbiddenExactHosts.Contains(normalizedHost) ||
            ForbiddenHostSuffixes.Any(suffix => normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            throw CreateBlockedException(isRedirect, "Access to local or internal network addresses is not permitted.");
        }

        if (IPAddress.TryParse(normalizedHost, out var ipLiteral))
        {
            if (IsForbiddenIpAddress(ipLiteral))
            {
                throw CreateBlockedException(isRedirect, "Access to private, loopback, or restricted IP addresses is not permitted.");
            }

            return;
        }

        // Reject unqualified single-label internal hostnames (e.g., "http://intranet/doc" or "http://redis:6379").
        if (!normalizedHost.Contains('.'))
        {
            throw CreateBlockedException(isRedirect, "Access to internal or unqualified host names is not permitted.");
        }
    }

    public static async Task<IPAddress[]> ValidateResolvedAddressesAsync(
        Uri uri,
        IDnsResolver dnsResolver,
        bool isRedirect,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(dnsResolver);

        ValidateUriSyntaxAndHost(uri, isRedirect);

        var normalizedHost = NormalizeHost(uri.DnsSafeHost);
        if (IPAddress.TryParse(normalizedHost, out var ipLiteral))
        {
            return [ipLiteral];
        }

        IPAddress[] resolved;
        try
        {
            resolved = await dnsResolver.GetHostAddressesAsync(normalizedHost, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ExternalDependencyAppException(
                "The target web host could not be resolved.",
                "URL_FETCH_FAILED");
        }

        if (resolved is null || resolved.Length == 0)
        {
            throw new ExternalDependencyAppException(
                "The target web host could not be resolved.",
                "URL_FETCH_FAILED");
        }

        foreach (var address in resolved)
        {
            if (IsForbiddenIpAddress(address))
            {
                throw CreateBlockedException(
                    isRedirect,
                    "The target address resolves to a restricted network destination and cannot be fetched.");
            }
        }

        return resolved;
    }

    public static bool IsForbiddenIpAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsForbiddenIPv4(address.GetAddressBytes());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsForbiddenIPv4(address.MapToIPv4().GetAddressBytes());
            }

            return IsForbiddenIPv6(address);
        }

        // Reject unknown address families by default.
        return true;
    }

    private static bool IsForbiddenIPv4(byte[] bytes)
    {
        if (bytes.Length != 4)
        {
            return true;
        }

        var b0 = bytes[0];
        var b1 = bytes[1];
        var b2 = bytes[2];

        // 0.0.0.0/8 — Unspecified / "this network"
        if (b0 == 0) return true;

        // 10.0.0.0/8 — RFC 1918 private
        if (b0 == 10) return true;

        // 100.64.0.0/10 — RFC 6598 Carrier-Grade NAT / shared space
        if (b0 == 100 && b1 >= 64 && b1 <= 127) return true;

        // 127.0.0.0/8 — Loopback
        if (b0 == 127) return true;

        // 169.254.0.0/16 — Link-local & cloud metadata (169.254.169.254)
        if (b0 == 169 && b1 == 254) return true;

        // 172.16.0.0/12 — RFC 1918 private
        if (b0 == 172 && b1 >= 16 && b1 <= 31) return true;

        // 192.0.0.0/24 — IETF protocol assignments
        if (b0 == 192 && b1 == 0 && b2 == 0) return true;

        // 192.0.2.0/24 — TEST-NET-1 documentation
        if (b0 == 192 && b1 == 0 && b2 == 2) return true;

        // 192.88.99.0/24 — 6to4 relay anycast (deprecated)
        if (b0 == 192 && b1 == 88 && b2 == 99) return true;

        // 192.168.0.0/16 — RFC 1918 private
        if (b0 == 192 && b1 == 168) return true;

        // 198.18.0.0/15 — Benchmarking
        if (b0 == 198 && (b1 == 18 || b1 == 19)) return true;

        // 198.51.100.0/24 — TEST-NET-2 documentation
        if (b0 == 198 && b1 == 51 && b2 == 100) return true;

        // 203.0.113.0/24 — TEST-NET-3 documentation
        if (b0 == 203 && b1 == 0 && b2 == 113) return true;

        // 224.0.0.0/4 — Multicast & 240.0.0.0/4 — Reserved / Broadcast (255.255.255.255)
        if (b0 >= 224) return true;

        return false;
    }

    private static bool IsForbiddenIPv6(IPAddress address)
    {
        if (IPAddress.IPv6Any.Equals(address) ||
            IPAddress.IPv6None.Equals(address) ||
            IPAddress.IPv6Loopback.Equals(address) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 16)
        {
            return true;
        }

        // fc00::/7 — Unique Local Address (private IPv6)
        if ((bytes[0] & 0xFE) == 0xFC) return true;

        // fe80::/10 — Link-local (defense-in-depth alongside IsIPv6LinkLocal)
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;

        // fec0::/10 — Deprecated site-local
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0) return true;

        // ff00::/8 — Multicast
        if (bytes[0] == 0xFF) return true;

        // 0100::/64 — Discard-only prefix
        if (bytes[0] == 0x01 && bytes[1] == 0x00 &&
            bytes[2] == 0 && bytes[3] == 0 &&
            bytes[4] == 0 && bytes[5] == 0 &&
            bytes[6] == 0 && bytes[7] == 0)
        {
            return true;
        }

        // 2001:db8::/32 — Documentation
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return true;

        // 2001:0000::/32 — Teredo (reject tunneling prefixes)
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00) return true;

        // 2001:0002::/48 — Benchmarking
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x02 &&
            bytes[4] == 0x00 && bytes[5] == 0x00)
        {
            return true;
        }

        // 2002::/16 — 6to4 (check embedded IPv4 in bytes[2..5])
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return IsForbiddenIPv4([bytes[2], bytes[3], bytes[4], bytes[5]]);
        }

        // 64:ff9b::/96 — NAT64 well-known prefix (check embedded IPv4 in bytes[12..15])
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B &&
            bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0 &&
            bytes[8] == 0 && bytes[9] == 0 && bytes[10] == 0 && bytes[11] == 0)
        {
            return IsForbiddenIPv4([bytes[12], bytes[13], bytes[14], bytes[15]]);
        }

        // ::/96 — Deprecated IPv4-compatible IPv6 (first 12 bytes zero)
        var firstTwelveZero = true;
        for (var i = 0; i < 12; i++)
        {
            if (bytes[i] != 0)
            {
                firstTwelveZero = false;
                break;
            }
        }

        if (firstTwelveZero)
        {
            return true;
        }

        return false;
    }

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.TrimEnd('.');
    }

    private static ValidationAppException CreateBlockedException(bool isRedirect, string message)
    {
        var code = isRedirect ? "URL_REDIRECT_BLOCKED" : "URL_SSRF_BLOCKED";
        return new ValidationAppException(
            message,
            new Dictionary<string, string[]> { ["url"] = [message] },
            code);
    }
}
