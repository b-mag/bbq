using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Carcosa.Server.P2P;

/// <summary>
/// Shared UDP socket bound to the listen port. Used for:
///   1. STUN (discover + keepalive of the NAT mapping)
///   2. Hole-punch hellos so a glyph can target the mapped UDP port
///   3. Mesh JSON after a punch succeeds
///
/// STUN packets (magic cookie 0x2112A442) are demuxed from JSON ('{').
/// </summary>
public sealed class UdpMeshTransport : IDisposable
{
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _keepaliveLoop;
    private IPEndPoint? _stunServer;
    private TaskCompletionSource<(string ip, int port)?>? _stunWaiter;
    private readonly ConcurrentDictionary<string, Action<PeerMessage>> _peers = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _punchWaiters = new();
    private int _started;

    public string? MappedAddress { get; private set; }
    public int LocalPort { get; private set; }
    public bool IsRunning => _udp != null;

    public event Action<IPEndPoint, PeerUdpPunchPayload>? OnInboundPunch;

    public void Start(int listenPort)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        LocalPort = listenPort;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
        _udp.Client.ReceiveBufferSize = 256 * 1024;
        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        Console.WriteLine($"[P2P:UDP] Listening on 0.0.0.0:{listenPort}");
    }

    /// <summary>
    /// Send a STUN binding request from the mesh socket so the NAT mapping
    /// is the same one friends will punch. Returns ip:mappedPort or null.
    /// </summary>
    public async Task<string?> DiscoverStunMappedAddressAsync(CancellationToken cancellationToken = default)
    {
        if (_udp == null)
            return null;

        foreach (var server in NatTraversalService.StunServers)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            var endpoint = NatTraversalService.ParseStunServer(server);
            if (endpoint == null) continue;

            try
            {
                var waiter = new TaskCompletionSource<(string ip, int port)?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stunWaiter = waiter;

                var request = NatTraversalService.CreateStunBindingRequest();
                await _udp.SendAsync(request, request.Length, endpoint);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(2000);
                using (timeout.Token.Register(() => waiter.TrySetResult(null)))
                {
                    var mapped = await waiter.Task;
                    if (mapped != null && !PeerAddress.IsLoopbackHost(mapped.Value.ip))
                    {
                        _stunServer = endpoint;
                        MappedAddress = PeerAddress.Compose(mapped.Value.ip, mapped.Value.port);
                        StartKeepalive();
                        return MappedAddress;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"[P2P:UDP] STUN {server} failed: {ex.Message}");
            }
            finally
            {
                _stunWaiter = null;
            }
        }

        return null;
    }

    public async Task<bool> PunchAsync(IPEndPoint remote, PeerIdentity local, CancellationToken cancellationToken)
    {
        if (_udp == null) return false;

        var key = EndpointKey(remote);
        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _punchWaiters[key] = waiter;

        var punch = new PeerMessage
        {
            Type = PeerMessageTypes.UdpPunch,
            UdpPunch = new PeerUdpPunchPayload
            {
                PeerId = local.PeerId,
                DisplayName = local.DisplayName,
                TcpAddress = local.PublicAddress,
                UdpAddress = local.StunMappedAddress,
                WorldId = local.WorldId,
                Ack = false,
            }
        };

        try
        {
            for (var i = 0; i < 8; i++)
            {
                if (cancellationToken.IsCancellationRequested) return false;
                await SendAsync(remote, punch);
                if (waiter.Task.IsCompletedSuccessfully && waiter.Task.Result)
                    return true;
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return waiter.Task.IsCompletedSuccessfully && waiter.Task.Result;
        }
        finally
        {
            _punchWaiters.TryRemove(key, out _);
        }
    }

    public void Register(IPEndPoint remote, Action<PeerMessage> deliver) =>
        _peers[EndpointKey(remote)] = deliver;

    public void Unregister(IPEndPoint remote) =>
        _peers.TryRemove(EndpointKey(remote), out _);

    public async Task<bool> SendAsync(IPEndPoint remote, PeerMessage message)
    {
        if (_udp == null) return false;
        try
        {
            var json = JsonSerializer.Serialize(message, PeerJsonContext.Default.PeerMessage);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _udp.SendAsync(bytes, bytes.Length, remote);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:UDP] Send to {remote} failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryParseEndpoint(string address, out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (!PeerAddress.TrySplit(address, out var host, out var port))
            return false;
        if (!IPAddress.TryParse(host, out var ip))
            return false;
        endpoint = new IPEndPoint(ip, port);
        return true;
    }

    public static bool IsStunPacket(byte[] data, int length) =>
        length >= 20 && data[4] == 0x21 && data[5] == 0x12 && data[6] == 0xA4 && data[7] == 0x42;

    private void StartKeepalive()
    {
        if (_keepaliveLoop != null || _cts == null) return;
        _keepaliveLoop = Task.Run(() => KeepaliveLoopAsync(_cts.Token));
    }

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                if (_udp == null || _stunServer == null) continue;
                var request = NatTraversalService.CreateStunBindingRequest();
                await _udp.SendAsync(request, request.Length, _stunServer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep the mapping alive on a best-effort basis.
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_udp == null) return;

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            var data = result.Buffer;
            if (data.Length == 0) continue;

            if (IsStunPacket(data, data.Length))
            {
                if (NatTraversalService.TryParseMappedEndpoint(data, out var ip, out var port) && ip != null)
                {
                    MappedAddress = PeerAddress.Compose(ip, port);
                    _stunWaiter?.TrySetResult((ip, port));
                }
                continue;
            }

            if (data[0] != (byte)'{')
                continue;

            PeerMessage? message;
            try
            {
                var json = Encoding.UTF8.GetString(data);
                message = JsonSerializer.Deserialize(json, PeerJsonContext.Default.PeerMessage);
            }
            catch
            {
                continue;
            }

            if (message == null) continue;

            var from = result.RemoteEndPoint;
            var key = EndpointKey(from);

            if (message.Type == PeerMessageTypes.UdpPunch && message.UdpPunch != null)
            {
                HandlePunch(from, key, message.UdpPunch);
                continue;
            }

            if (_peers.TryGetValue(key, out var deliver))
            {
                try { deliver(message); }
                catch { /* peer callback must not kill the receive loop */ }
            }
        }
    }

    private void HandlePunch(IPEndPoint from, string key, PeerUdpPunchPayload punch)
    {
        if (punch.Ack)
        {
            if (_punchWaiters.TryGetValue(key, out var waiter))
                waiter.TrySetResult(true);
            return;
        }

        var ack = new PeerMessage
        {
            Type = PeerMessageTypes.UdpPunch,
            UdpPunch = new PeerUdpPunchPayload
            {
                PeerId = punch.PeerId,
                DisplayName = punch.DisplayName,
                TcpAddress = punch.TcpAddress,
                UdpAddress = punch.UdpAddress,
                WorldId = punch.WorldId,
                Ack = true,
            }
        };
        _ = SendAsync(from, ack);

        try { OnInboundPunch?.Invoke(from, punch); }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:UDP] Inbound punch handler failed: {ex.Message}");
        }
    }

    private static string EndpointKey(IPEndPoint endpoint) => endpoint.ToString();

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _udp?.Dispose();
        _udp = null;
    }
}
