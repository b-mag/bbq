// =============================================================================
// PeerConnection.cs — Single Peer-to-Peer WebSocket Connection
// =============================================================================
//
// OVERVIEW:
// Represents one directional link to a remote peer in the mesh. Handles:
//   - Sending messages (thread-safe via semaphore)
//   - Receiving messages (blocking loop on a dedicated async task)
//   - Connection lifecycle (connect, handshake, active, disconnect)
//   - Keepalive monitoring (detect dead peers)
//
// DUAL ROLE:
// A PeerConnection can be either:
//   - OUTBOUND: We initiated the connection (we're the WebSocket client)
//   - INBOUND: The remote peer connected to us (we accepted their WebSocket)
// Both behave identically after the handshake completes. The distinction only
// matters during setup (who sends handshake first).
//
// THREAD SAFETY:
// - SendAsync uses a SemaphoreSlim(1,1) to serialize writes (WebSocket is not
//   thread-safe for concurrent sends)
// - The receive loop runs on its own async task
// - State transitions (Connected → Disconnected) are atomic via Interlocked
//
// LIFECYCLE:
//   Created → Handshaking → Active → Disconnected
//   Any error at any stage → Disconnected (triggers OnDisconnected event)
// =============================================================================

using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Carcosa.Server.P2P;

/// <summary>
/// Connection states for a peer link.
/// </summary>
public enum PeerConnectionState
{
    /// <summary>Connection created but not yet established.</summary>
    Created,
    /// <summary>WebSocket connected, handshake in progress.</summary>
    Handshaking,
    /// <summary>Handshake complete, actively exchanging messages.</summary>
    Active,
    /// <summary>Connection closed (terminal state).</summary>
    Disconnected
}

/// <summary>
/// Represents a single WebSocket connection to a remote peer.
/// Manages the full lifecycle from connection through disconnection.
/// </summary>
public sealed class PeerConnection : IDisposable
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private WebSocket? _webSocket;
    private UdpMeshTransport? _udp;
    private IPEndPoint? _udpRemote;
    private Channel<PeerMessage>? _udpInbox;
    private CancellationTokenSource? _cts;
    private int _state = (int)PeerConnectionState.Created;
    private int _disposed;

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>Unique peer ID of the remote peer (set after handshake).</summary>
    public string RemotePeerId { get; private set; } = "";

    /// <summary>Display name of the remote peer (set after handshake).</summary>
    public string RemoteDisplayName { get; private set; } = "";

    /// <summary>Public address of the remote peer (for Peer Exchange).</summary>
    public string RemoteAddress { get; private set; } = "";

    /// <summary>World shard of the remote peer.</summary>
    public string RemoteWorldId { get; private set; } = "";

    /// <summary>Whether this is an outbound (we initiated) or inbound connection.</summary>
    public bool IsOutbound { get; init; }

    /// <summary>True when this link is the UDP hole-punched path, not WebSocket.</summary>
    public bool IsUdp => _udp != null;

    /// <summary>Current connection state.</summary>
    public PeerConnectionState State =>
        (PeerConnectionState)Interlocked.CompareExchange(ref _state, 0, 0);

    /// <summary>When this connection was established.</summary>
    public DateTime ConnectedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>Last time we received any message from this peer.</summary>
    public DateTime LastMessageAt { get; private set; } = DateTime.UtcNow;

    /// <summary>Round-trip time in milliseconds (from keepalive).</summary>
    public int LatencyMs { get; private set; }

    /// <summary>Total bytes sent on this connection.</summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>Total bytes received on this connection.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    private long _bytesSent;
    private long _bytesReceived;

    // =========================================================================
    // EVENTS
    // =========================================================================

    /// <summary>Fired when a complete message is received from the remote peer.</summary>
    public event Action<PeerConnection, PeerMessage>? OnMessageReceived;

    /// <summary>Fired when the connection is lost (clean or abrupt).</summary>
    public event Action<PeerConnection>? OnDisconnected;

    // =========================================================================
    // CONNECTION SETUP
    // =========================================================================

    /// <summary>
    /// Initialize with an already-accepted inbound WebSocket.
    /// Used when a remote peer connects to our /ws/peer endpoint.
    /// </summary>
    public void AcceptInbound(WebSocket webSocket)
    {
        _webSocket = webSocket;
        _cts = new CancellationTokenSource();
        TransitionState(PeerConnectionState.Handshaking);
        ConnectedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Attach to the shared UDP mesh socket after a hole punch. Receive is
    /// demuxed by remote endpoint into an inbox channel.
    /// </summary>
    public void AttachUdp(UdpMeshTransport transport, IPEndPoint remote)
    {
        _udp = transport;
        _udpRemote = remote;
        _udpInbox = Channel.CreateUnbounded<PeerMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _cts = new CancellationTokenSource();
        transport.Register(remote, DeliverUdp);
        TransitionState(PeerConnectionState.Handshaking);
        ConnectedAt = DateTime.UtcNow;
    }

    internal void DeliverUdp(PeerMessage message)
    {
        LastMessageAt = DateTime.UtcNow;
        _udpInbox?.Writer.TryWrite(message);
    }

    /// <summary>
    /// Connect outbound to a remote peer at the given address.
    /// Establishes the WebSocket connection.
    /// </summary>
    /// <param name="address">Remote peer address (host:port).</param>
    /// <param name="cancellationToken">Cancellation token for the connect attempt.</param>
    /// <returns>True if WebSocket connection succeeded, false otherwise.</returns>
    public async Task<bool> ConnectOutboundAsync(string address, CancellationToken cancellationToken)
    {
        try
        {
            var client = new ClientWebSocket();
            var uri = new Uri($"ws://{address}/ws/peer");

            await client.ConnectAsync(uri, cancellationToken);

            _webSocket = client;
            _cts = new CancellationTokenSource();
            TransitionState(PeerConnectionState.Handshaking);
            ConnectedAt = DateTime.UtcNow;

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:Connection] Failed to connect to {address}: {ex.Message}");
            TransitionState(PeerConnectionState.Disconnected);
            return false;
        }
    }

    /// <summary>
    /// Set the remote peer's identity (called after successful handshake).
    /// Transitions the connection to Active state.
    /// </summary>
    public void SetRemoteIdentity(string peerId, string displayName, string address, string worldId)
    {
        RemotePeerId = peerId;
        RemoteDisplayName = displayName;
        RemoteAddress = address;
        RemoteWorldId = worldId;
        TransitionState(PeerConnectionState.Active);
    }

    // =========================================================================
    // MESSAGE SEND/RECEIVE
    // =========================================================================

    /// <summary>
    /// Receive exactly one message from the remote peer. Used during handshake
    /// before the main receive loop starts. Returns null on error/timeout.
    /// </summary>
    public async Task<PeerMessage?> ReceiveSingleAsync(CancellationToken ct)
    {
        if (_udpInbox != null)
        {
            try
            {
                return await _udpInbox.Reader.ReadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return null;

        var buffer = new byte[8192];
        try
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                LastMessageAt = DateTime.UtcNow;
                Interlocked.Add(ref _bytesReceived, result.Count);
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                return JsonSerializer.Deserialize(json, PeerJsonContext.Default.PeerMessage);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }

        return null;
    }

    /// <summary>
    /// Send a message to the remote peer. Thread-safe (serialized via semaphore).
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>True if sent successfully, false if connection is dead.</returns>
    public async Task<bool> SendAsync(PeerMessage message)
    {
        if (_udp != null && _udpRemote != null)
        {
            var sent = await _udp.SendAsync(_udpRemote, message);
            if (sent)
            {
                var payload = JsonSerializer.Serialize(message, PeerJsonContext.Default.PeerMessage);
                Interlocked.Add(ref _bytesSent, payload.Length);
            }
            return sent;
        }

        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            return false;

        var json = JsonSerializer.Serialize(message, PeerJsonContext.Default.PeerMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await _sendLock.WaitAsync();
            try
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    _cts?.Token ?? CancellationToken.None);
                Interlocked.Add(ref _bytesSent, bytes.Length);
                return true;
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception)
        {
            await DisconnectAsync("send_failed");
            return false;
        }
    }

    /// <summary>
    /// Start the receive loop. Blocks until the connection closes.
    /// Call this on a background task after handshake completes.
    /// </summary>
    public async Task ReceiveLoopAsync()
    {
        if (_udpInbox != null && _cts != null)
        {
            try
            {
                await foreach (var message in _udpInbox.Reader.ReadAllAsync(_cts.Token))
                {
                    LastMessageAt = DateTime.UtcNow;
                    OnMessageReceived?.Invoke(this, message);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                await DisconnectAsync("connection_closed");
            }
            return;
        }

        if (_webSocket == null || _cts == null) return;

        var buffer = new byte[8192];

        try
        {
            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    LastMessageAt = DateTime.UtcNow;
                    Interlocked.Add(ref _bytesReceived, result.Count);
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JsonSerializer.Deserialize(json, PeerJsonContext.Default.PeerMessage);

                    if (message != null)
                    {
                        OnMessageReceived?.Invoke(this, message);
                    }
                }
            }
        }
        catch (WebSocketException) { /* Remote peer disconnected abruptly */ }
        catch (OperationCanceledException) { /* Local shutdown */ }
        finally
        {
            await DisconnectAsync("connection_closed");
        }
    }

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Gracefully disconnect from the remote peer.
    /// </summary>
    /// <param name="reason">Human-readable reason for disconnection.</param>
    public async Task DisconnectAsync(string reason = "normal")
    {
        if (!TransitionState(PeerConnectionState.Disconnected))
            return; // Already disconnected

        Console.WriteLine($"[P2P:Connection] Disconnected from {RemotePeerId} ({reason})");

        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }

        if (_udpRemote != null)
            _udp?.Unregister(_udpRemote);
        _udpInbox?.Writer.TryComplete();

        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
        }
        catch { /* Best-effort close */ }

        OnDisconnected?.Invoke(this);
    }

    /// <summary>
    /// Update the measured latency (called when keepalive ack is received).
    /// </summary>
    public void UpdateLatency(int rttMs)
    {
        LatencyMs = rttMs;
    }

    /// <summary>
    /// Check if this connection has timed out (no messages received recently).
    /// </summary>
    public bool IsTimedOut =>
        (DateTime.UtcNow - LastMessageAt).TotalSeconds > PeerProtocol.PeerTimeoutSeconds;

    // =========================================================================
    // STATE MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Atomically transition to a new state. Returns false if already in target state
    /// or in a terminal state (Disconnected).
    /// </summary>
    private bool TransitionState(PeerConnectionState newState)
    {
        var current = Interlocked.CompareExchange(ref _state, (int)newState, _state);
        if (current == (int)PeerConnectionState.Disconnected && newState != PeerConnectionState.Disconnected)
            return false; // Can't transition out of Disconnected
        Interlocked.Exchange(ref _state, (int)newState);
        return true;
    }

    // =========================================================================
    // DISPOSAL
    // =========================================================================

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (_udpRemote != null)
            _udp?.Unregister(_udpRemote);
        _udpInbox?.Writer.TryComplete();
        _cts?.Dispose();
        _cts = null;
        try { _webSocket?.Dispose(); }
        catch { }
        _sendLock.Dispose();
    }
}
