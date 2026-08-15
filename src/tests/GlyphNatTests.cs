using Carcosa.Server.P2P;
using Xunit;

namespace Carcosa.Tests;

public class GlyphCodecTests
{
    [Fact]
    public void GenerateForPeer_EncodesIpv4ListenAddressWithoutThrowing()
    {
        var identity = new PeerIdentity
        {
            PeerId = "abcd1234abcd1234",
            PublicAddress = "8.8.8.8:5000",
            WorldId = "carcosa-00",
            ListenPort = 5000,
        };

        var glyph = GlyphCodec.GenerateForPeer(identity);

        Assert.False(string.IsNullOrWhiteSpace(glyph));
        Assert.DoesNotContain("ERROR", glyph);
        var decoded = GlyphCodec.DecodeToAddress(glyph);
        Assert.NotNull(decoded);
        Assert.Equal("8.8.8.8:5000", decoded.Value.address);
        Assert.Equal((byte)0, decoded.Value.worldIndex);
    }

    [Fact]
    public void GenerateForPeer_UsesShardIndexNotHash()
    {
        var identity = new PeerIdentity
        {
            PeerId = "abcd1234abcd1234",
            PublicAddress = "1.2.3.4:5000",
            WorldId = "carcosa-0a",
            ListenPort = 5000,
        };

        var glyph = GlyphCodec.GenerateForPeer(identity);
        var decoded = GlyphCodec.DecodeToAddress(glyph);

        Assert.NotNull(decoded);
        Assert.Equal((byte)10, decoded.Value.worldIndex);
    }

    [Fact]
    public void EncodeV3_RoundtripsEphemeralUdpPort()
    {
        var glyph = GlyphCodec.EncodeV3("70.127.46.77", 54321, 0);
        var decoded = GlyphCodec.DecodeToAddress(glyph);
        Assert.NotNull(decoded);
        Assert.Equal("70.127.46.77:54321", decoded.Value.address);
    }

    [Fact]
    public void GenerateForPeer_PrefersStunMappedAddress()
    {
        var identity = new PeerIdentity
        {
            PeerId = "abcd1234abcd1234",
            PublicAddress = "70.127.46.77:5000",
            StunMappedAddress = "70.127.46.77:54321",
            WorldId = "carcosa-00",
            ListenPort = 5000,
        };

        var glyph = GlyphCodec.GenerateForPeer(identity);
        var decoded = GlyphCodec.DecodeToAddress(glyph);
        Assert.NotNull(decoded);
        Assert.Equal("70.127.46.77:54321", decoded.Value.address);
    }

    [Fact]
    public void DecodeToAddress_StillAcceptsV2Glyphs()
    {
        var glyph = GlyphCodec.EncodeV2("8.8.8.8", 5000, 0);
        var decoded = GlyphCodec.DecodeToAddress(glyph);
        Assert.NotNull(decoded);
        Assert.Equal("8.8.8.8:5000", decoded.Value.address);
    }
}

public class PeerAddressTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    public void IsPublicUnicastIpv4_ClassifiesRanges(string ip, bool expected)
    {
        Assert.Equal(expected, PeerAddress.IsPublicUnicastIpv4(ip));
    }

    [Fact]
    public void IsSelfAddress_DetectsLoopbackOnListenPort()
    {
        var identity = new PeerIdentity
        {
            PeerId = "abcd1234abcd1234",
            PublicAddress = "8.8.8.8:5000",
            ListenPort = 5000,
        };

        Assert.True(PeerAddress.IsSelfAddress("127.0.0.1:5000", identity));
        Assert.True(PeerAddress.IsSelfAddress("8.8.8.8:5000", identity));
        Assert.False(PeerAddress.IsSelfAddress("9.9.9.9:5000", identity));
    }

    [Fact]
    public void IsSelfAddress_DetectsStunMappedUdpPort()
    {
        var identity = new PeerIdentity
        {
            PeerId = "abcd1234abcd1234",
            PublicAddress = "8.8.8.8:5000",
            StunMappedAddress = "8.8.8.8:54321",
            ListenPort = 5000,
        };

        Assert.True(PeerAddress.IsSelfAddress("8.8.8.8:54321", identity));
        Assert.False(PeerAddress.IsSelfAddress("8.8.8.8:9", identity));
    }

    [Fact]
    public void NormalizeManualAddress_FillsListenPortWhenOmitted()
    {
        Assert.Equal("1.2.3.4:5000", PeerAddress.NormalizeManualAddress("1.2.3.4", 5000));
        Assert.Equal("1.2.3.4:6000", PeerAddress.NormalizeManualAddress("1.2.3.4:6000", 5000));
    }
}

public class UpnpPortMapperTests
{
    [Fact]
    public void FindServiceControlUrl_ReadsWanIpConnection()
    {
        const string xml = """
            <root>
              <service>
                <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                <controlURL>/upnp/control/WANIPConn1</controlURL>
              </service>
            </root>
            """;

        var url = UpnpPortMapper.FindServiceControlUrl(xml, "urn:schemas-upnp-org:service:WANIPConnection:1");
        Assert.Equal("/upnp/control/WANIPConn1", url);
    }

    [Fact]
    public void ParseSsdpLocation_IsCaseInsensitive()
    {
        var location = UpnpPortMapper.ParseSsdpLocation(
            "HTTP/1.1 200 OK\r\nLocation: http://192.168.1.1:1900/rootDesc.xml\r\n\r\n");
        Assert.Equal("http://192.168.1.1:1900/rootDesc.xml", location);
    }

    [Fact]
    public void TryFindWanService_ResolvesRelativeControlUrl()
    {
        const string xml = """
            <root>
              <service>
                <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                <controlURL>/ctl</controlURL>
              </service>
            </root>
            """;

        var found = UpnpPortMapper.TryFindWanService(xml, "http://192.168.1.1:1900/root.xml", out var controlUrl, out var serviceType);

        Assert.True(found);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", serviceType);
        Assert.Equal("http://192.168.1.1:1900/ctl", controlUrl);
    }
}

public class PeerConnectionDisposeTests
{
    [Fact]
    public void Dispose_IsIdempotent()
    {
        var connection = new PeerConnection();
        connection.Dispose();
        connection.Dispose();
    }
}

public class UdpMeshTransportTests
{
    [Fact]
    public void IsStunPacket_DetectsMagicCookie()
    {
        var packet = new byte[20];
        packet[4] = 0x21; packet[5] = 0x12; packet[6] = 0xA4; packet[7] = 0x42;
        Assert.True(UdpMeshTransport.IsStunPacket(packet, packet.Length));
        Assert.False(UdpMeshTransport.IsStunPacket("{}"u8.ToArray(), 2));
    }

    [Fact]
    public async Task PunchAsync_LoopbackAcks()
    {
        var portA = BindEphemeralUdpPort();
        var portB = BindEphemeralUdpPort();

        using var a = new UdpMeshTransport();
        using var b = new UdpMeshTransport();
        a.Start(portA);
        b.Start(portB);

        var inbound = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.OnInboundPunch += (_, punch) => inbound.TrySetResult(punch.PeerId);

        var identity = new PeerIdentity
        {
            PeerId = "aaaaaaaaaaaaaaaa",
            DisplayName = "A",
            PublicAddress = $"127.0.0.1:{portA}",
            ListenPort = portA,
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var acked = await a.PunchAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, portB), identity, cts.Token);

        Assert.True(acked);
        Assert.Equal("aaaaaaaaaaaaaaaa", await inbound.Task.WaitAsync(cts.Token));
    }

    private static int BindEphemeralUdpPort()
    {
        using var probe = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
