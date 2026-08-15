using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Carcosa.Server.P2P;

/// <summary>
/// Helpers for the address advertised in Glyphs and tracker registration.
/// Glyphs always encode IPv4 + the local TCP listen port (never a STUN UDP mapping).
/// </summary>
public static class PeerAddress
{
    public static string Compose(string ip, int port) => $"{ip}:{port}";

    public static bool TrySplit(string address, out string host, out int port)
    {
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(address)) return false;

        var parts = address.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out port)) return false;
        host = parts[0];
        return !string.IsNullOrWhiteSpace(host);
    }

    public static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host is "::1" or "0.0.0.0" or "::") return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    public static bool IsLoopbackAddress(string address) =>
        TrySplit(address, out var host, out _) && IsLoopbackHost(host);

    /// <summary>
    /// True if this IPv4 is globally routable (not loopback, RFC1918, APIPA, CGNAT, or multicast).
    /// </summary>
    public static bool IsPublicUnicastIpv4(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var b = ip.GetAddressBytes();
        if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
        if (b[0] == 169 && b[1] == 254) return false;
        if (b[0] == 192 && b[1] == 168) return false;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
        if (b[0] >= 224) return false;
        return true;
    }

    /// <summary>
    /// Prefer a NIC that has an IPv4 gateway (the LAN address UPnP must map).
    /// </summary>
    public static string? TryGetLocalIpv4()
    {
        string? fallback = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(g.Address));

            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(addr.Address)) continue;
                var b = addr.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;

                var ip = addr.Address.ToString();
                if (hasGateway) return ip;
                fallback ??= ip;
            }
        }

        return fallback;
    }

    public static string NormalizeManualAddress(string value, int listenPort)
    {
        value = value.Trim();
        if (TrySplit(value, out var host, out var port))
            return Compose(host, port);

        if (IPAddress.TryParse(value, out _))
            return Compose(value, listenPort);

        return Compose(value, listenPort);
    }

    public static bool IsSelfAddress(string address, PeerIdentity identity)
    {
        if (!TrySplit(address, out var host, out var port)) return false;

        if (port == identity.ListenPort && IsLoopbackHost(host))
            return true;

        if (TrySplit(identity.PublicAddress, out var publicHost, out var publicPort)
            && publicPort == port
            && host.Equals(publicHost, StringComparison.OrdinalIgnoreCase))
            return true;

        if (TrySplit(identity.StunMappedAddress, out var stunHost, out var stunPort)
            && stunPort == port
            && host.Equals(stunHost, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
