using Carcosa.Server.P2P;
using Xunit;

namespace Carcosa.Tests;

public class NatTraversalServiceTests
{
    [Fact]
    public void TryParseMappedAddress_ParsesIpv4StunResponse()
    {
        var response = CreateBindingSuccessResponse(new byte[] { 127, 0, 0, 1 }, 60000);

        var success = NatTraversalService.TryParseMappedAddress(response, out var mappedAddress);

        Assert.True(success);
        Assert.Equal("127.0.0.1:60000", mappedAddress);
    }

    [Fact]
    public void TryParseMappedAddress_ReturnsFalse_WhenNoMappedAddressPresent()
    {
        var response = new byte[]
        {
            0x01, 0x01, // STUN Binding Success Response
            0x00, 0x0C, // length: 12 bytes
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

    private static byte[] CreateBindingSuccessResponse(byte[] addressBytes, ushort port)
    {
        var txId = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC };
        var mappedAddress = new byte[]
        {
            0x00, // reserved
            0x01, // IPv4 family
            (byte)(port >> 8), (byte)(port & 0xFF),
            addressBytes[0], addressBytes[1], addressBytes[2], addressBytes[3]
        };

        var attrHeader = new byte[]
        {
            0x00, 0x01, // ATTRIBUTE: MAPPED-ADDRESS
            0x00, 0x08, // length
        };

        var message = new byte[20 + attrHeader.Length + mappedAddress.Length];
        message[0] = 0x01; message[1] = 0x01; // Binding Success Response
        message[2] = 0x00; message[3] = (byte)(attrHeader.Length + mappedAddress.Length);
        message[4] = 0x21; message[5] = 0x12; message[6] = 0xA4; message[7] = 0x42;

        Array.Copy(txId, 0, message, 8, txId.Length);

        var offset = 20;
        Array.Copy(attrHeader, 0, message, offset, attrHeader.Length);
        offset += attrHeader.Length;
        Array.Copy(mappedAddress, 0, message, offset, mappedAddress.Length);

        return message;
    }
}
