using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Carcosa.Server.P2P;

/// <summary>
/// Discovers the address advertised in Glyphs. STUN runs on the shared UDP
/// mesh socket so the mapped port is the one friends punch. TCP listen port
/// stays on PublicAddress for tracker / WebSocket fallback.
/// </summary>
public sealed class NatTraversalService
{
    public static readonly string[] StunServers =
    [
        "stun.l.google.com:19302",
        "stun1.l.google.com:19302",
        "stun2.l.google.com:19302",
        "stun3.l.google.com:19302",
        "stun4.l.google.com:19302",
    ];

    private const uint StunMagicCookie = 0x2112A442;
    private const ushort StunBindingSuccess = 0x0101;
    private const ushort AttrMappedAddress = 0x0001;
    private const ushort AttrXorMappedAddress = 0x0020;

    private readonly UpnpPortMapper _upnp;
    private readonly UdpMeshTransport? _udp;

    public NatTraversalService() : this(new UpnpPortMapper(), null) { }

    public NatTraversalService(UpnpPortMapper upnp, UdpMeshTransport? udp = null)
    {
        _upnp = upnp ?? throw new ArgumentNullException(nameof(upnp));
        _udp = udp;
    }

    /// <summary>
    /// UPnP-map the listen port, STUN-discover the public mapping on the UDP
    /// mesh socket, set PublicAddress to ip:listenPort (TCP) and StunMappedAddress
    /// to the UDP mapping used in Glyphs.
    /// </summary>
    public async Task<string> DiscoverAndApplyAsync(
        PeerIdentity identity,
        int listenPort,
        string? manualPublicAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (identity == null) throw new ArgumentNullException(nameof(identity));

        if (!string.IsNullOrWhiteSpace(manualPublicAddress))
        {
            identity.PublicAddress = PeerAddress.NormalizeManualAddress(manualPublicAddress, listenPort);
            identity.StunMappedAddress = identity.PublicAddress;
            Console.WriteLine($"[P2P:NAT] Using manual public address: {identity.PublicAddress}");
            try
            {
                _udp?.Start(listenPort);
                await _upnp.TryMapTcpPortAsync(listenPort, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:NAT] UPnP mapping failed: {ex.Message}");
            }

            return identity.PublicAddress;
        }

        try { _udp?.Start(listenPort); }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:NAT] UDP mesh bind failed: {ex.Message}");
        }

        string? upnpIp = null;
        try
        {
            var upnp = await _upnp.TryMapTcpPortAsync(listenPort, cancellationToken);
            if (upnp.Mapped)
            {
                upnpIp = upnp.ExternalIp;
                Console.WriteLine("[P2P:NAT] UPnP TCP port mapping succeeded");
            }
            else
            {
                Console.WriteLine("[P2P:NAT] UPnP port mapping not available (router may need a manual TCP forward)");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:NAT] UPnP failed: {ex.Message}");
        }

        string? stunIp = null;
        string? stunMapped = null;
        try
        {
            if (_udp != null)
            {
                stunMapped = await _udp.DiscoverStunMappedAddressAsync(cancellationToken);
                if (!string.IsNullOrEmpty(stunMapped) && PeerAddress.TrySplit(stunMapped, out var mappedHost, out _))
                {
                    stunIp = mappedHost;
                    Console.WriteLine($"[P2P:NAT] STUN mapped UDP {stunMapped} (glyph will use this port, TCP fallback {listenPort})");
                }
            }

            if (string.IsNullOrEmpty(stunIp))
            {
                stunIp = await TryDiscoverPublicIpAsync(cancellationToken);
                if (!string.IsNullOrEmpty(stunIp))
                    Console.WriteLine($"[P2P:NAT] STUN public IP: {stunIp} (no mapped UDP socket; glyph will use TCP port {listenPort})");
                else
                    Console.WriteLine("[P2P:NAT] STUN discovery failed");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:NAT] STUN failed: {ex.Message}");
        }

        var ip = SelectAdvertisedIp(stunIp, upnpIp) ?? PeerAddress.TryGetLocalIpv4();
        if (string.IsNullOrEmpty(ip))
        {
            Console.WriteLine("[P2P:NAT] No public or LAN IPv4 found; glyphs will encode loopback (remote joins will fail)");
            ip = "127.0.0.1";
        }
        else if (!PeerAddress.IsPublicUnicastIpv4(ip))
        {
            Console.WriteLine($"[P2P:NAT] Advertising LAN address {ip}:{listenPort}. Remote internet joins need UPnP or a manual TCP {listenPort} forward.");
        }

        identity.PublicAddress = PeerAddress.Compose(ip, listenPort);
        identity.StunMappedAddress = !string.IsNullOrEmpty(stunMapped)
            ? stunMapped
            : identity.PublicAddress;
        return identity.PublicAddress;
    }

    /// <summary>
    /// Query public STUN servers for the public IPv4 on a throwaway socket.
    /// Used when the mesh UDP socket could not discover a mapping.
    /// </summary>
    public async Task<string?> TryDiscoverPublicIpAsync(CancellationToken cancellationToken = default)
    {
        foreach (var server in StunServers)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            try
            {
                var endpoint = ParseStunServer(server);
                if (endpoint == null) continue;

                using var udpClient = new UdpClient();
                udpClient.Client.ReceiveTimeout = 2000;
                udpClient.Client.SendTimeout = 2000;

                var request = CreateStunBindingRequest();
                await udpClient.SendAsync(request, request.Length, endpoint.Address.ToString(), endpoint.Port);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(2000);

                var result = await udpClient.ReceiveAsync(timeoutCts.Token);
                if (TryParseMappedEndpoint(result.Buffer, out var ip, out _) &&
                    !string.IsNullOrEmpty(ip) &&
                    !PeerAddress.IsLoopbackHost(ip))
                {
                    return ip;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                // Per-server timeout — try the next STUN host.
            }
            catch (SocketException)
            {
                // Ignore and continue to the next STUN server.
            }
            catch (Exception)
            {
                // Ignore and continue to the next STUN server.
            }
        }

        return null;
    }

    /// <summary>Kept for tests and older call sites. Prefer <see cref="TryParseMappedEndpoint"/>.</summary>
    public static bool TryParseMappedAddress(byte[] response, out string? mappedAddress)
    {
        if (TryParseMappedEndpoint(response, out var ip, out var port) && ip != null)
        {
            mappedAddress = PeerAddress.Compose(ip, port);
            return true;
        }

        mappedAddress = null;
        return false;
    }

    /// <summary>
    /// Parses a STUN Binding Success response. Prefers XOR-MAPPED-ADDRESS (RFC 5389),
    /// which is what Google public STUN servers send.
    /// </summary>
    public static bool TryParseMappedEndpoint(byte[] response, out string? ip, out int port)
    {
        ip = null;
        port = 0;
        if (response == null || response.Length < 20)
            return false;

        var messageType = (ushort)((response[0] << 8) | response[1]);
        if (messageType != StunBindingSuccess)
            return false;

        var messageLength = (ushort)((response[2] << 8) | response[3]);
        if (messageLength > 0 && response.Length < 20 + messageLength)
            return false;

        string? xorIp = null;
        int xorPort = 0;
        string? mappedIp = null;
        int mappedPort = 0;

        var offset = 20;
        var end = messageLength > 0 ? Math.Min(response.Length, 20 + messageLength) : response.Length;
        while (offset + 4 <= end)
        {
            var attributeType = (ushort)((response[offset] << 8) | response[offset + 1]);
            var attributeLength = (ushort)((response[offset + 2] << 8) | response[offset + 3]);
            var valueOffset = offset + 4;
            var valueEnd = valueOffset + attributeLength;
            if (valueEnd > response.Length)
                break;

            if (attributeType == AttrXorMappedAddress)
            {
                if (TryReadAddress(response, valueOffset, attributeLength, xorWithCookie: true, out var parsedIp, out var parsedPort))
                {
                    xorIp = parsedIp;
                    xorPort = parsedPort;
                }
            }
            else if (attributeType is AttrMappedAddress or 0x0002)
            {
                if (TryReadAddress(response, valueOffset, attributeLength, xorWithCookie: false, out var parsedIp, out var parsedPort))
                {
                    mappedIp = parsedIp;
                    mappedPort = parsedPort;
                }
            }

            offset = valueEnd;
            var pad = (4 - (attributeLength % 4)) % 4;
            offset += pad;
        }

        if (xorIp != null)
        {
            ip = xorIp;
            port = xorPort;
            return true;
        }

        if (mappedIp != null)
        {
            ip = mappedIp;
            port = mappedPort;
            return true;
        }

        return false;
    }

    public static string? SelectAdvertisedIp(string? stunIp, string? upnpIp)
    {
        if (!string.IsNullOrEmpty(stunIp) && PeerAddress.IsPublicUnicastIpv4(stunIp))
            return stunIp;
        if (!string.IsNullOrEmpty(upnpIp) && PeerAddress.IsPublicUnicastIpv4(upnpIp))
            return upnpIp;
        if (!string.IsNullOrEmpty(upnpIp) && !PeerAddress.IsLoopbackHost(upnpIp))
            return upnpIp;
        if (!string.IsNullOrEmpty(stunIp) && !PeerAddress.IsLoopbackHost(stunIp))
            return stunIp;
        return null;
    }

    private static bool TryReadAddress(
        byte[] response, int valueOffset, int attributeLength, bool xorWithCookie, out string? ip, out int port)
    {
        ip = null;
        port = 0;
        if (attributeLength < 8)
            return false;

        var family = response[valueOffset + 1];
        if (family != 0x01) // IPv4 only — Glyphs cannot encode IPv6
            return false;

        var rawPort = (ushort)((response[valueOffset + 2] << 8) | response[valueOffset + 3]);
        var b0 = response[valueOffset + 4];
        var b1 = response[valueOffset + 5];
        var b2 = response[valueOffset + 6];
        var b3 = response[valueOffset + 7];

        if (xorWithCookie)
        {
            rawPort ^= (ushort)(StunMagicCookie >> 16);
            b0 ^= 0x21;
            b1 ^= 0x12;
            b2 ^= 0xA4;
            b3 ^= 0x42;
        }

        port = rawPort;
        ip = $"{b0}.{b1}.{b2}.{b3}";
        return true;
    }

    public static IPEndPoint? ParseStunServer(string server)
    {
        var parts = server.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            return null;

        var addresses = Dns.GetHostAddresses(parts[0]);
        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        return ip == null ? null : new IPEndPoint(ip, port);
    }

    public static byte[] CreateStunBindingRequest()
    {
        var request = new byte[20];
        request[0] = 0x00;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = 0x00;
        request[4] = 0x21;
        request[5] = 0x12;
        request[6] = 0xA4;
        request[7] = 0x42;
        RandomNumberGenerator.Fill(request.AsSpan(8, 12));
        return request;
    }
}
