using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Carcosa.Server.P2P;

/// <summary>
/// Result of an attempt to map the local TCP listen port through an IGD.
/// </summary>
public readonly record struct UpnpMapResult(bool Mapped, string? ExternalIp, string? InternalIp);

/// <summary>
/// Lightweight UPnP IGD client (SSDP + SOAP). Native-AOT safe: HTTP and string
/// parsing only — no XML serializers. Opens the TCP listen port so Glyph joins
/// can reach this process the same way BitTorrent maps its listen port.
/// </summary>
public sealed class UpnpPortMapper
{
    private static readonly string[] SearchTargets =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
    ];

    /// <summary>
    /// Discover an Internet Gateway Device and map TCP <paramref name="listenPort"/>
    /// from the WAN to this machine. Best-effort: failures return Mapped=false.
    /// </summary>
    public async Task<UpnpMapResult> TryMapTcpPortAsync(int listenPort, CancellationToken cancellationToken = default)
    {
        var internalIp = PeerAddress.TryGetLocalIpv4();
        if (internalIp == null)
            return default;

        var locations = await DiscoverGatewayLocationsAsync(cancellationToken);
        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var descriptionXml = await GetStringAsync(location, cancellationToken);
                if (string.IsNullOrEmpty(descriptionXml)) continue;

                if (!TryFindWanService(descriptionXml, location, out var controlUrl, out var serviceType)
                    || controlUrl == null || serviceType == null)
                {
                    continue;
                }

                var mapped = await AddPortMappingAsync(
                    controlUrl, serviceType, listenPort, internalIp, cancellationToken);
                if (!mapped) continue;

                var externalIp = await GetExternalIpAsync(controlUrl, serviceType, cancellationToken);
                Console.WriteLine($"[P2P:UPnP] Mapped TCP {listenPort} → {internalIp}:{listenPort}" +
                    (string.IsNullOrEmpty(externalIp) ? "" : $" (WAN {externalIp})"));
                return new UpnpMapResult(true, externalIp, internalIp);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:UPnP] Gateway at {location} failed: {ex.Message}");
            }
        }

        return default;
    }

    public static bool TryFindWanService(string deviceXml, string location, out string? controlUrl, out string? serviceType)
    {
        controlUrl = null;
        serviceType = null;

        string[] types =
        [
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        ];

        foreach (var type in types)
        {
            var relative = FindServiceControlUrl(deviceXml, type);
            if (string.IsNullOrEmpty(relative)) continue;

            if (!Uri.TryCreate(location, UriKind.Absolute, out var baseUri))
                continue;

            controlUrl = Uri.TryCreate(baseUri, relative, out var resolved)
                ? resolved.ToString()
                : relative;
            serviceType = type;
            return true;
        }

        return false;
    }

    public static string? FindServiceControlUrl(string xml, string serviceType)
    {
        var typeIdx = xml.IndexOf(serviceType, StringComparison.OrdinalIgnoreCase);
        if (typeIdx < 0) return null;

        var serviceStart = xml.LastIndexOf("<service", typeIdx, StringComparison.OrdinalIgnoreCase);
        var serviceEnd = xml.IndexOf("</service>", typeIdx, StringComparison.OrdinalIgnoreCase);
        if (serviceStart < 0 || serviceEnd < 0 || serviceEnd < typeIdx)
            return ExtractTag(xml[typeIdx..], "controlURL");

        return ExtractTag(xml[serviceStart..serviceEnd], "controlURL");
    }

    internal static string? ExtractTag(string xml, string tag)
    {
        var open = xml.IndexOf($"<{tag}", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var gt = xml.IndexOf('>', open);
        if (gt < 0) return null;
        var close = xml.IndexOf($"</{tag}>", gt, StringComparison.OrdinalIgnoreCase);
        if (close < 0) return null;
        return xml[(gt + 1)..close].Trim();
    }

    public static string? ParseSsdpLocation(string response)
    {
        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                return line["LOCATION:".Length..].Trim();
        }

        return null;
    }

    private async Task<List<string>> DiscoverGatewayLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 2000;
            client.EnableBroadcast = true;

            var multicast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            foreach (var target in SearchTargets)
            {
                var payload = Encoding.ASCII.GetBytes(
                    "M-SEARCH * HTTP/1.1\r\n" +
                    "HOST: 239.255.255.250:1900\r\n" +
                    "MAN: \"ssdp:discover\"\r\n" +
                    "MX: 2\r\n" +
                    $"ST: {target}\r\n" +
                    "\r\n");
                await client.SendAsync(payload, payload.Length, multicast);
            }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(remaining);
                try
                {
                    var result = await client.ReceiveAsync(timeoutCts.Token);
                    var text = Encoding.ASCII.GetString(result.Buffer);
                    var location = ParseSsdpLocation(text);
                    if (!string.IsNullOrEmpty(location))
                        locations.Add(location);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (SocketException)
        {
            // No multicast permission / no IGD on this network.
        }

        return locations.ToList();
    }

    private static async Task<bool> AddPortMappingAsync(
        string controlUrl, string serviceType, int listenPort, string internalIp, CancellationToken cancellationToken)
    {
        var body =
            "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            $"<u:AddPortMapping xmlns:u=\"{serviceType}\">" +
            "<NewRemoteHost></NewRemoteHost>" +
            $"<NewExternalPort>{listenPort}</NewExternalPort>" +
            "<NewProtocol>TCP</NewProtocol>" +
            $"<NewInternalPort>{listenPort}</NewInternalPort>" +
            $"<NewInternalClient>{internalIp}</NewInternalClient>" +
            "<NewEnabled>1</NewEnabled>" +
            "<NewPortMappingDescription>Carcosa P2P</NewPortMappingDescription>" +
            "<NewLeaseDuration>0</NewLeaseDuration>" +
            "</u:AddPortMapping>" +
            "</s:Body></s:Envelope>";

        var (status, response) = await SoapAsync(controlUrl, serviceType, "AddPortMapping", body, cancellationToken);
        if (status >= 200 && status < 300) return true;

        // 718 ConflictInMappingEntry — port already mapped (often by a previous run).
        if (response.Contains("718", StringComparison.Ordinal) ||
            response.Contains("ConflictInMapping", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static async Task<string?> GetExternalIpAsync(
        string controlUrl, string serviceType, CancellationToken cancellationToken)
    {
        var body =
            "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            $"<u:GetExternalIPAddress xmlns:u=\"{serviceType}\">" +
            "</u:GetExternalIPAddress>" +
            "</s:Body></s:Envelope>";

        var (status, response) = await SoapAsync(controlUrl, serviceType, "GetExternalIPAddress", body, cancellationToken);
        if (status < 200 || status >= 300) return null;
        return ExtractTag(response, "NewExternalIPAddress");
    }

    private static async Task<string?> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var http = CreateHttp();
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<(int status, string body)> SoapAsync(
        string controlUrl, string serviceType, string action, string body, CancellationToken cancellationToken)
    {
        using var http = CreateHttp();
        using var request = new HttpRequestMessage(HttpMethod.Post, controlUrl);
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{serviceType}#{action}\"");
        request.Content = new StringContent(body, Encoding.UTF8, "text/xml");
        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode, text);
    }

    private static HttpClient CreateHttp() => new() { Timeout = TimeSpan.FromSeconds(3) };
}
