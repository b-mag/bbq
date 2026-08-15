// =============================================================================
// PeerMesh.cs — P2P Mesh Network Manager
// =============================================================================
//
// OVERVIEW:
// The PeerMesh is the central coordinator for all peer-to-peer connections.
// It manages:
//   - Inbound connections (other peers connecting to us)
//   - Outbound connections (us connecting to discovered peers)
//   - Handshake orchestration (validate + establish identity)
//   - Message routing (deliver incoming messages to subscribers)
//   - Mesh health (keepalive, timeout detection, reconnection)
//
// ARCHITECTURE:
// The PeerMesh is a SINGLETON within each Carcosa.Server instance. It owns
// the /ws/peer WebSocket endpoint and all PeerConnection instances.
//
//   ┌──────────────────────────────────────────────────────────────┐
//   │ PeerMesh                                                     │
//   │                                                              │
//   │  /ws/peer endpoint ──► AcceptInbound() ──► PeerConnection    │
//   │                                              │               │
//   │  ConnectToPeer() ────► ConnectOutbound() ──► PeerConnection  │
//   │                                              │               │
//   │  All PeerConnections ───► OnPeerMessage event                │
//   │                           (subscribers handle routing)       │
//   └──────────────────────────────────────────────────────────────┘
//
// THREAD SAFETY:
// - ConcurrentDictionary for the connection registry
// - Each PeerConnection handles its own send serialization
// - Events fire on the receiving connection's thread (subscribers must be aware)
//
// MESH TOPOLOGY:
// Full mesh — every peer connects to every other peer. With a 100-peer cap,
// this means up to 99 connections per peer. WebSocket connections are lightweight
// (kernel handles buffering), and we only send state updates for nearby players.
// =============================================================================

using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace Carcosa.Server.P2P;

/// <summary>
/// Manages the full P2P mesh network. Handles inbound/outbound connections,
/// handshake orchestration, and message routing to subscribers.
/// </summary>
public sealed class PeerMesh
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerIdentity _localIdentity;
    private readonly UdpMeshTransport? _udp;
    private readonly ConcurrentDictionary<string, PeerConnection> _peers = new();
    private readonly HashSet<string> _connectingAddresses = new(); // Prevent duplicate outbound attempts
    private readonly object _connectingLock = new();
    private readonly HashSet<string> _inboundUdpKeys = new();

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>Our local peer identity.</summary>
    public PeerIdentity LocalIdentity => _localIdentity;

    /// <summary>Number of currently active peer connections.</summary>
    public int PeerCount => _peers.Count;

    /// <summary>All connected peer IDs.</summary>
    public IEnumerable<string> ConnectedPeerIds => _peers.Keys;

    /// <summary>All active peer connections (for iteration).</summary>
    public IEnumerable<PeerConnection> Connections => _peers.Values;

    // =========================================================================
    // EVENTS
    // =========================================================================

    /// <summary>
    /// Fired when a new peer successfully completes handshake and joins the mesh.
    /// Subscribers receive the PeerConnection with identity set.
    /// </summary>
    public event Action<PeerConnection>? OnPeerJoined;

    /// <summary>
    /// Fired when a peer disconnects from the mesh (clean or abrupt).
    /// </summary>
    public event Action<PeerConnection>? OnPeerLeft;

    /// <summary>
    /// Fired for every message received from any peer (after handshake).
    /// This is the main dispatch point for state sync, chat, party, etc.
    /// </summary>
    public event Action<PeerConnection, PeerMessage>? OnPeerMessage;

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    public PeerMesh(PeerIdentity localIdentity) : this(localIdentity, null) { }

    public PeerMesh(PeerIdentity localIdentity, UdpMeshTransport? udp)
    {
        _localIdentity = localIdentity;
        _udp = udp;
        if (_udp != null)
            _udp.OnInboundPunch += HandleInboundUdpPunch;
    }

    // =========================================================================
    // INBOUND CONNECTION HANDLING
    // =========================================================================

    /// <summary>
    /// Handle a new inbound WebSocket connection from a remote peer.
    /// Called from the /ws/peer endpoint handler.
    /// Performs handshake, then enters receive loop. Blocks until disconnect.
    /// </summary>
    /// <param name="webSocket">The accepted WebSocket connection.</param>
    /// <param name="cancellationToken">Server shutdown token.</param>
    public async Task HandleInboundPeerAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var connection = new PeerConnection { IsOutbound = false };
        connection.AcceptInbound(webSocket);

        try
        {
            // Wait for the remote peer to send their handshake
            var handshakeResult = await WaitForHandshakeAsync(connection, cancellationToken);

            if (!handshakeResult)
            {
                await connection.DisconnectAsync("handshake_failed");
                return;
            }

            // Register in the mesh
            RegisterPeer(connection);

            // Enter receive loop (blocks until disconnect)
            await connection.ReceiveLoopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:Mesh] Inbound peer error: {ex.Message}");
            await connection.DisconnectAsync("error");
        }
        finally
        {
            UnregisterPeer(connection);
            connection.Dispose();
        }
    }

    // =========================================================================
    // OUTBOUND CONNECTION HANDLING
    // =========================================================================

    /// <summary>
    /// Initiate a connection to a remote peer at the given address.
    /// Performs handshake, then enters receive loop on a background task.
    /// </summary>
    /// <param name="address">Remote peer address (host:port).</param>
    /// <returns>True if connection and handshake succeeded.</returns>
    public async Task<bool> ConnectToPeerAsync(string address)
    {
        // Prevent duplicate connection attempts to the same address
        lock (_connectingLock)
        {
            if (_connectingAddresses.Contains(address))
                return false;
            _connectingAddresses.Add(address);
        }

        try
        {
            // Check if we're already connected to a peer at this address
            if (_peers.Values.Any(p => p.RemoteAddress == address))
            {
                Console.WriteLine($"[P2P:Mesh] Already connected to {address}, skipping");
                return false;
            }

            var connection = new PeerConnection { IsOutbound = true };

            if (_udp != null && await TryConnectUdpAsync(connection, address))
            {
                RegisterPeer(connection);
                _ = Task.Run(async () =>
                {
                    try { await connection.ReceiveLoopAsync(); }
                    finally
                    {
                        UnregisterPeer(connection);
                        connection.Dispose();
                    }
                });
                return true;
            }

            connection.Dispose();
            connection = new PeerConnection { IsOutbound = true };

            // Establish WebSocket connection (TCP fallback)
            using var cts = new CancellationTokenSource(PeerProtocol.HandshakeTimeoutMs);
            if (!await connection.ConnectOutboundAsync(address, cts.Token))
            {
                connection.Dispose();
                return false;
            }

            // Send our handshake
            var handshakeMsg = PeerHandshake.CreateHandshakeMessage(_localIdentity);
            if (!await connection.SendAsync(handshakeMsg))
            {
                await connection.DisconnectAsync("handshake_send_failed");
                connection.Dispose();
                return false;
            }

            // Wait for handshake response
            var accepted = await WaitForHandshakeResponseAsync(connection, cts.Token);
            if (!accepted)
            {
                connection.Dispose();
                return false;
            }

            // Register in the mesh
            RegisterPeer(connection);

            // Start receive loop on background task
            _ = Task.Run(async () =>
            {
                try
                {
                    await connection.ReceiveLoopAsync();
                }
                finally
                {
                    UnregisterPeer(connection);
                    connection.Dispose();
                }
            });

            return true;
        }
        finally
        {
            lock (_connectingLock)
            {
                _connectingAddresses.Remove(address);
            }
        }
    }

    private async Task<bool> TryConnectUdpAsync(PeerConnection connection, string address)
    {
        if (_udp == null || !UdpMeshTransport.TryParseEndpoint(address, out var remote) || remote == null)
            return false;

        Console.WriteLine($"[P2P:UDP] Punching {address}...");
        using var punchCts = new CancellationTokenSource(3000);
        if (!await _udp.PunchAsync(remote, _localIdentity, punchCts.Token))
        {
            Console.WriteLine($"[P2P:UDP] No punch ack from {address}, falling back to TCP");
            return false;
        }

        connection.AttachUdp(_udp, remote);
        var handshakeMsg = PeerHandshake.CreateHandshakeMessage(_localIdentity);
        if (!await connection.SendAsync(handshakeMsg))
        {
            await connection.DisconnectAsync("udp_handshake_send_failed");
            return false;
        }

        using var hsCts = new CancellationTokenSource(PeerProtocol.HandshakeTimeoutMs);
        var accepted = await WaitForHandshakeResponseAsync(connection, hsCts.Token);
        if (!accepted)
        {
            await connection.DisconnectAsync("udp_handshake_failed");
            return false;
        }

        Console.WriteLine($"[P2P:UDP] Mesh path up to {address}");
        return true;
    }

    private void HandleInboundUdpPunch(IPEndPoint from, PeerUdpPunchPayload punch)
    {
        if (_udp == null) return;
        if (punch.PeerId == _localIdentity.PeerId) return;
        if (IsPeerConnected(punch.PeerId)) return;

        var key = from.ToString();
        lock (_connectingLock)
        {
            if (_connectingAddresses.Contains(PeerAddress.Compose(from.Address.ToString(), from.Port)))
                return;
            if (!_inboundUdpKeys.Add(key))
                return;
        }

        // Attach on the receive thread so the joiner's handshake is not dropped.
        var connection = new PeerConnection { IsOutbound = false };
        connection.AttachUdp(_udp, from);
        Console.WriteLine($"[P2P:UDP] Inbound punch from {from} ({punch.DisplayName})");

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(PeerProtocol.HandshakeTimeoutMs);
                var ok = await WaitForHandshakeAsync(connection, cts.Token);
                if (!ok)
                {
                    await connection.DisconnectAsync("udp_handshake_failed");
                    return;
                }

                RegisterPeer(connection);
                await connection.ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:UDP] Inbound peer error: {ex.Message}");
                await connection.DisconnectAsync("error");
            }
            finally
            {
                UnregisterPeer(connection);
                connection.Dispose();
                lock (_connectingLock)
                    _inboundUdpKeys.Remove(key);
            }
        });
    }

    // =========================================================================
    // BROADCASTING
    // =========================================================================

    /// <summary>
    /// Send a message to all connected peers.
    /// Failures on individual connections are silently ignored (they'll be
    /// cleaned up by the timeout/disconnect detection).
    /// </summary>
    public async Task BroadcastAsync(PeerMessage message)
    {
        var tasks = _peers.Values
            .Where(p => p.State == PeerConnectionState.Active)
            .Select(p => p.SendAsync(message))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Send a message to all connected peers except one (e.g., don't echo back to sender).
    /// </summary>
    public async Task BroadcastExceptAsync(string excludePeerId, PeerMessage message)
    {
        var tasks = _peers.Values
            .Where(p => p.State == PeerConnectionState.Active && p.RemotePeerId != excludePeerId)
            .Select(p => p.SendAsync(message))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Send a message to a specific peer by their peer ID.
    /// </summary>
    public async Task<bool> SendToPeerAsync(string peerId, PeerMessage message)
    {
        if (_peers.TryGetValue(peerId, out var connection) && connection.State == PeerConnectionState.Active)
        {
            return await connection.SendAsync(message);
        }
        return false;
    }

    /// <summary>
    /// Get a connected peer by ID.
    /// </summary>
    public PeerConnection? GetPeer(string peerId)
    {
        _peers.TryGetValue(peerId, out var peer);
        return peer;
    }

    /// <summary>
    /// Check if a peer ID is currently connected.
    /// </summary>
    public bool IsPeerConnected(string peerId) => _peers.ContainsKey(peerId);

    // =========================================================================
    // HANDSHAKE HELPERS
    // =========================================================================

    /// <summary>
    /// Wait for an inbound peer to send their handshake, validate it, and respond.
    /// Uses direct single-message receive (no event loop needed).
    /// </summary>
    private async Task<bool> WaitForHandshakeAsync(PeerConnection connection, CancellationToken ct)
    {
        // Read exactly one message (the handshake)
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(PeerProtocol.HandshakeTimeoutMs);

        var message = await connection.ReceiveSingleAsync(timeoutCts.Token);

        if (message == null || message.Type != PeerMessageTypes.Handshake || message.Handshake == null)
        {
            Console.WriteLine("[P2P:Mesh] Handshake timeout or invalid first message");
            return false;
        }

        // Validate the handshake
        var validationResult = PeerHandshake.Validate(
            message.Handshake,
            _localIdentity,
            _peers.Count,
            IsPeerConnected);

        // Send our response
        var response = PeerHandshake.CreateHandshakeResponse(
            _localIdentity, validationResult, _peers.Count);
        await connection.SendAsync(response);

        if (!validationResult.Accepted)
        {
            Console.WriteLine($"[P2P:Mesh] Rejected peer {message.Handshake.PeerId}: " +
                $"{validationResult.RejectionReason}");
            return false;
        }

        // Handshake accepted — set remote identity
        connection.SetRemoteIdentity(
            message.Handshake.PeerId,
            message.Handshake.DisplayName,
            message.Handshake.PublicAddress,
            message.Handshake.WorldId);

        Console.WriteLine($"[P2P:Mesh] Accepted inbound peer: {message.Handshake.DisplayName} " +
            $"({message.Handshake.PeerId})");

        return true;
    }

    /// <summary>
    /// Wait for a handshake response after we've sent our handshake (outbound).
    /// Uses direct single-message receive.
    /// </summary>
    private async Task<bool> WaitForHandshakeResponseAsync(PeerConnection connection, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(PeerProtocol.HandshakeTimeoutMs);

        var message = await connection.ReceiveSingleAsync(timeoutCts.Token);

        if (message == null || message.Type != PeerMessageTypes.HandshakeResponse || message.HandshakeResponse == null)
        {
            Console.WriteLine("[P2P:Mesh] Handshake response timeout or invalid message");
            return false;
        }

        if (!message.HandshakeResponse.Accepted)
        {
            Console.WriteLine($"[P2P:Mesh] Handshake rejected: {message.HandshakeResponse.RejectionReason}");
            return false;
        }

        // Set remote identity from response
        connection.SetRemoteIdentity(
            message.HandshakeResponse.PeerId,
            message.HandshakeResponse.DisplayName,
            message.HandshakeResponse.PublicAddress,
            _localIdentity.WorldId);

        Console.WriteLine($"[P2P:Mesh] Connected to peer: {message.HandshakeResponse.DisplayName} " +
            $"({message.HandshakeResponse.PeerId})");

        return true;
    }

    // =========================================================================
    // PEER REGISTRY
    // =========================================================================

    /// <summary>
    /// Register a peer connection in the mesh after successful handshake.
    /// Subscribes to disconnect events and notifies listeners.
    /// </summary>
    private void RegisterPeer(PeerConnection connection)
    {
        if (string.IsNullOrEmpty(connection.RemotePeerId))
        {
            Console.WriteLine("[P2P:Mesh] Cannot register peer with empty ID");
            return;
        }

        // If a peer with same ID already exists, disconnect the old one
        if (_peers.TryRemove(connection.RemotePeerId, out var existing))
        {
            Console.WriteLine($"[P2P:Mesh] Replacing existing connection for {connection.RemotePeerId}");
            _ = existing.DisconnectAsync("replaced");
            existing.Dispose();
        }

        _peers[connection.RemotePeerId] = connection;

        // Subscribe to messages for routing
        connection.OnMessageReceived += (conn, msg) =>
        {
            OnPeerMessage?.Invoke(conn, msg);
        };

        // Subscribe to disconnect for cleanup
        connection.OnDisconnected += (conn) =>
        {
            UnregisterPeer(conn);
        };

        Console.WriteLine($"[P2P:Mesh] Peer registered: {connection.RemoteDisplayName} " +
            $"({connection.RemotePeerId}). Total peers: {_peers.Count}");

        OnPeerJoined?.Invoke(connection);
    }

    /// <summary>
    /// Remove a peer from the mesh (on disconnect or kick).
    /// </summary>
    private void UnregisterPeer(PeerConnection connection)
    {
        if (string.IsNullOrEmpty(connection.RemotePeerId)) return;

        if (_peers.TryRemove(connection.RemotePeerId, out _))
        {
            Console.WriteLine($"[P2P:Mesh] Peer unregistered: {connection.RemoteDisplayName} " +
                $"({connection.RemotePeerId}). Total peers: {_peers.Count}");

            OnPeerLeft?.Invoke(connection);
        }
    }

    // =========================================================================
    // MESH HEALTH
    // =========================================================================

    /// <summary>
    /// Check all connections for timeouts and remove dead peers.
    /// Should be called periodically (e.g., every few seconds).
    /// </summary>
    public async Task CheckConnectionHealthAsync()
    {
        var deadPeers = _peers.Values
            .Where(p => p.IsTimedOut || p.State == PeerConnectionState.Disconnected)
            .ToList();

        foreach (var peer in deadPeers)
        {
            Console.WriteLine($"[P2P:Mesh] Peer timed out: {peer.RemoteDisplayName} ({peer.RemotePeerId})");
            await peer.DisconnectAsync("timeout");
        }
    }

    /// <summary>
    /// Send keepalive pings to all connected peers.
    /// </summary>
    public async Task SendKeepalivesAsync()
    {
        var keepalive = new PeerMessage
        {
            Type = PeerMessageTypes.Keepalive,
            Keepalive = new PeerKeepalivePayload
            {
                Sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        };

        await BroadcastAsync(keepalive);
    }

    /// <summary>
    /// Get a summary of all connected peers (for Peer Exchange messages).
    /// </summary>
    public PeerEndpoint[] GetPeerEndpoints()
    {
        return _peers.Values
            .Where(p => p.State == PeerConnectionState.Active && !string.IsNullOrEmpty(p.RemoteAddress))
            .Select(p => new PeerEndpoint
            {
                PeerId = p.RemotePeerId,
                Address = p.RemoteAddress,
                DisplayName = p.RemoteDisplayName,
                WorldId = p.RemoteWorldId,
            })
            .ToArray();
    }

    // =========================================================================
    // SHUTDOWN
    // =========================================================================

    /// <summary>
    /// Gracefully disconnect from all peers and shut down the mesh.
    /// </summary>
    public async Task ShutdownAsync()
    {
        Console.WriteLine($"[P2P:Mesh] Shutting down ({_peers.Count} peers)...");

        var disconnectTasks = _peers.Values
            .Select(p => p.DisconnectAsync("shutdown"))
            .ToArray();

        await Task.WhenAll(disconnectTasks);

        foreach (var peer in _peers.Values)
        {
            peer.Dispose();
        }

        _peers.Clear();
        Console.WriteLine("[P2P:Mesh] Shutdown complete");
    }
}
