using Carcosa.Server.P2P;
using Xunit;

namespace Carcosa.Tests;

public class NatTraversalServiceTests
{
    [Fact]
    public void TryParseMappedAddress_ParsesIpv4StunResponse()
    {
        var response = CreateMappedAddressResponse(new byte[] { 127, 0, 0, 1 }, 60000);

        var success = NatTraversalService.TryParseMappedAddress(response, out var mappedAddress);

        Assert.True(success);
        Assert.Equal("127.0.0.1:60000", mappedAddress);
    }

    [Fact]
    public void TryParseMappedEndpoint_ParsesXorMappedAddress()
    {
        var response = CreateXorMappedAddressResponse(new byte[] { 73, 148, 92, 211 }, 54321);

        var success = NatTraversalService.TryParseMappedEndpoint(response, out var ip, out var port);

        Assert.True(success);
        Assert.Equal("73.148.92.211", ip);
        Assert.Equal(54321, port);
    }

    [Fact]
    public void TryParseMappedEndpoint_PrefersXorMappedAddressOverMappedAddress()
    {
        var response = CreateResponseWithBoth(
            mappedIp: new byte[] { 1, 2, 3, 4 },
            mappedPort: 9,
            xorIp: new byte[] { 8, 8, 8, 8 },
            xorPort: 5000);

        var success = NatTraversalService.TryParseMappedEndpoint(response, out var ip, out var port);

        Assert.True(success);
        Assert.Equal("8.8.8.8", ip);
        Assert.Equal(5000, port);
    }

    [Fact]
    public void TryParseMappedEndpoint_SkipsPaddedAttributes()
    {
        var response = CreateXorMappedAddressAfterPaddedSoftware(new byte[] { 1, 1, 1, 1 }, 5000);

        var success = NatTraversalService.TryParseMappedEndpoint(response, out var ip, out var port);

        Assert.True(success);
        Assert.Equal("1.1.1.1", ip);
        Assert.Equal(5000, port);
    }

    [Fact]
    public void TryParseMappedAddress_ReturnsFalse_WhenNoMappedAddressPresent()
    {
        var response = new byte[]
        {
            0x01, 0x01,
            0x00, 0x0C,
            0x21, 0x12, 0xA4, 0x42,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };

        var success = NatTraversalService.TryParseMappedAddress(response, out var mappedAddress);

        Assert.False(success);
        Assert.Null(mappedAddress);
    }

    [Theory]
    [InlineData("8.8.8.8", null, "8.8.8.8")]
    [InlineData(null, "1.1.1.1", "1.1.1.1")]
    [InlineData("10.0.0.5", "73.1.2.3", "73.1.2.3")]
    [InlineData("8.8.8.8", "9.9.9.9", "8.8.8.8")]
    [InlineData("127.0.0.1", "192.168.1.1", "192.168.1.1")]
    public void SelectAdvertisedIp_PrefersPublicStunThenPublicUpnp(string? stun, string? upnp, string expected)
    {
        Assert.Equal(expected, NatTraversalService.SelectAdvertisedIp(stun, upnp));
    }

    [Fact]
    public void SelectAdvertisedIp_ReturnsNull_WhenNothingUsable()
    {
        Assert.Null(NatTraversalService.SelectAdvertisedIp("127.0.0.1", null));
    }

    private static byte[] CreateMappedAddressResponse(byte[] addressBytes, ushort port)
    {
        return CreateBindingSuccess(BuildMappedAttribute(0x0001, addressBytes, port, xor: false));
    }

    private static byte[] CreateXorMappedAddressResponse(byte[] addressBytes, ushort port)
    {
        return CreateBindingSuccess(BuildMappedAttribute(0x0020, addressBytes, port, xor: true));
    }

    private static byte[] CreateResponseWithBoth(byte[] mappedIp, ushort mappedPort, byte[] xorIp, ushort xorPort)
    {
        var mapped = BuildMappedAttribute(0x0001, mappedIp, mappedPort, xor: false);
        var xor = BuildMappedAttribute(0x0020, xorIp, xorPort, xor: true);
        var attrs = new byte[mapped.Length + xor.Length];
        Buffer.BlockCopy(mapped, 0, attrs, 0, mapped.Length);
        Buffer.BlockCopy(xor, 0, attrs, mapped.Length, xor.Length);
        return CreateBindingSuccess(attrs);
    }

    private static byte[] CreateXorMappedAddressAfterPaddedSoftware(byte[] addressBytes, ushort port)
    {
        // SOFTWARE "x" (length 1) + 3 bytes padding, then XOR-MAPPED-ADDRESS
        var software = new byte[]
        {
            0x80, 0x22, // SOFTWARE
            0x00, 0x01, // length 1
            (byte)'x', 0x00, 0x00, 0x00
        };
        var xor = BuildMappedAttribute(0x0020, addressBytes, port, xor: true);
        var attrs = new byte[software.Length + xor.Length];
        Buffer.BlockCopy(software, 0, attrs, 0, software.Length);
        Buffer.BlockCopy(xor, 0, attrs, software.Length, xor.Length);
        return CreateBindingSuccess(attrs);
    }

    private static byte[] BuildMappedAttribute(ushort type, byte[] addressBytes, ushort port, bool xor)
    {
        var encodedPort = port;
        var b0 = addressBytes[0];
        var b1 = addressBytes[1];
        var b2 = addressBytes[2];
        var b3 = addressBytes[3];
        if (xor)
        {
            encodedPort ^= 0x2112;
            b0 ^= 0x21;
            b1 ^= 0x12;
            b2 ^= 0xA4;
            b3 ^= 0x42;
        }

        return
        [
            (byte)(type >> 8), (byte)(type & 0xFF),
            0x00, 0x08,
            0x00, 0x01,
            (byte)(encodedPort >> 8), (byte)(encodedPort & 0xFF),
            b0, b1, b2, b3
        ];
    }

    private static byte[] CreateBindingSuccess(byte[] attributes)
    {
        var message = new byte[20 + attributes.Length];
        message[0] = 0x01;
        message[1] = 0x01;
        message[2] = (byte)(attributes.Length >> 8);
        message[3] = (byte)(attributes.Length & 0xFF);
        message[4] = 0x21;
        message[5] = 0x12;
        message[6] = 0xA4;
        message[7] = 0x42;
        Buffer.BlockCopy(attributes, 0, message, 20, attributes.Length);
        return message;
    }
}
