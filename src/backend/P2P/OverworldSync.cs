// =============================================================================
// OverworldSync.cs — P2P Overworld State Synchronization
// =============================================================================
//
// OVERVIEW:
// In the P2P architecture, each peer is authoritative over their OWN player.
// This module handles:
//   1. Broadcasting our local player's state to all mesh peers every tick
//   2. Receiving and storing remote peer states for rendering
//   3. Providing the local frontend with a merged view of all players
//
// KEY DESIGN PRINCIPLE — INPUT AUTHORITY:
// Unlike client-server where the server validates all inputs, in P2P each peer
// controls their own player position. Other peers RENDER that position and can
// VALIDATE it (anti-cheat), but they cannot modify it. This means:
//   - No reconciliation needed for remote players (trust their position)
//   - Local player uses client-side prediction as before
//   - Anti-cheat layer (Task 6) validates remote positions post-hoc
//
// DATA FLOW:
//   ┌─────────────────────────────────────────────────────────────┐
//   │ LOCAL PEER (this instance)                                   │
//   │                                                             │
//   │  Player Input ──► Local Simulation ──► Local Player State   │
//   │                                            │                │
//   │                                            ▼                │
//   │                                     BroadcastState()        │
//   │                                       (to all peers)        │
//   │                                                             │
//   │  Remote Peer Messages ──► ReceiveRemoteState()              │
//   │                                   │                         │
//   │                                   ▼                         │
//   │                           _remotePlayers map                 │
//   │                                   │                         │
//   │                                   ▼                         │
//   │                     GetAllPlayers() (frontend queries this)  │
//   └─────────────────────────────────────────────────────────────┘
//
// TICK RATE:
// State is broadcast at 20Hz (matching the existing game loop). Remote players
// are interpolated on the frontend between updates using velocity hints.
//
// THREAD SAFETY:
// - _remotePlayers uses ConcurrentDictionary (reads from frontend thread,
//   writes from peer message handlers on connection threads)
// - Local player state is written by the game loop thread and read by the
//   broadcast timer — protected via volatile/Interlocked where needed
// =============================================================================

using System.Collections.Concurrent;

namespace Carcosa.Server.P2P;

/// <summary>
/// Represents a remote player's state as received from their peer.
/// Stored locally for rendering by the frontend.
/// </summary>
public sealed class RemotePlayerState
{
    /// <summary>Peer ID of the remote player.</summary>
    public required string PeerId { get; init; }

    /// <summary>Player display name.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>World X position (tile coordinates).</summary>
    public float X { get; set; }

    /// <summary>World Y position.</summary>
    public float Y { get; set; }

    /// <summary>Velocity X (tiles/tick) for interpolation.</summary>
    public float VelocityX { get; set; }

    /// <summary>Velocity Y.</summary>
    public float VelocityY { get; set; }

    /// <summary>Player status: "exploring", "in_party", "in_dungeon".</summary>
    public string Status { get; set; } = "exploring";

    /// <summary>Party ID (if in a party).</summary>
    public string? PartyId { get; set; }

    /// <summary>Whether this player is party leader.</summary>
    public bool IsPartyLeader { get; set; }

    /// <summary>Cosmetic body id: a, b, or c.</summary>
    public string Figure { get; set; } = "b";

    /// <summary>When this state was last updated (local clock).</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>The remote timestamp from the peer's state update.</summary>
    public long RemoteTimestamp { get; set; }
}

/// <summary>
/// Manages overworld state synchronization across the P2P mesh.
/// Each peer broadcasts their own state and collects remote peer states.
/// </summary>
public sealed class OverworldSync
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly ConcurrentDictionary<string, RemotePlayerState> _remotePlayers = new();
    private readonly CancellationTokenSource _cts = new();
    private PeerValidator? _validator;
    private Task? _broadcastTask;

    // Chat message buffer (recent messages for frontend polling)
    private readonly List<ChatMessageEntry> _chatMessages = new();
    private readonly object _chatLock = new();

    // Admin message buffer
    private readonly List<AdminMessageEntry> _adminMessages = new();
    private readonly object _adminLock = new();

    // Local player state (written by game input, read by broadcast timer)
    private float _localX;
    private float _localY;
    private float _localVelocityX;
    private float _localVelocityY;
    private string _localStatus = "exploring";
    private string? _localPartyId;
    private bool _localIsPartyLeader;
    private bool _localDirty; // Only broadcast when something changed
    private bool _localRelocate; // Next broadcast is a trusted origin reset

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>All remote players currently tracked (for frontend rendering).</summary>
    public IReadOnlyDictionary<string, RemotePlayerState> RemotePlayers => _remotePlayers;

    /// <summary>Total players visible (local + remote non-dungeon).</summary>
    public int VisiblePlayerCount =>
        _remotePlayers.Values.Count(p => p.Status != "in_dungeon") + 1; // +1 for local

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    public OverworldSync(PeerMesh mesh, PeerIdentity localIdentity)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;

        // Subscribe to mesh events
        _mesh.OnPeerMessage += HandlePeerMessage;
        _mesh.OnPeerLeft += HandlePeerLeft;
        _mesh.OnPeerJoined += HandlePeerJoined;
    }

    /// <summary>
    /// Set the validator for anti-cheat checking on incoming state updates.
    /// Called after both OverworldSync and PeerValidator are constructed.
    /// </summary>
    public void SetValidator(PeerValidator validator)
    {
        _validator = validator;
    }

    // =========================================================================
    // LOCAL PLAYER STATE UPDATES
    // =========================================================================

    /// <summary>
    /// Update the local player's position (called by the local game input system).
    /// This state will be broadcast to all peers on the next tick.
    /// </summary>
    public void UpdateLocalPosition(float x, float y, float velocityX, float velocityY, bool relocate = false)
    {
        if (relocate || _localX != x || _localY != y || _localVelocityX != velocityX || _localVelocityY != velocityY)
        {
            _localX = x;
            _localY = y;
            _localVelocityX = velocityX;
            _localVelocityY = velocityY;
            _localDirty = true;
            if (relocate) _localRelocate = true;
        }
    }

    /// <summary>
    /// Update the local player's status (exploring, in_party, in_dungeon).
    /// </summary>
    public void UpdateLocalStatus(string status, string? partyId = null, bool isPartyLeader = false)
    {
        if (_localStatus != status || _localPartyId != partyId || _localIsPartyLeader != isPartyLeader)
        {
            _localStatus = status;
            _localPartyId = partyId;
            _localIsPartyLeader = isPartyLeader;
            _localDirty = true;
        }
    }

    /// <summary>Force a state broadcast (e.g. after name/figure is chosen).</summary>
    public void MarkLocalDirty() => _localDirty = true;

    // =========================================================================
    // BROADCAST LOOP
    // =========================================================================

    /// <summary>
    /// Start the periodic state broadcast loop (20Hz).
    /// </summary>
    public void Start()
    {
        _broadcastTask = Task.Run(() => BroadcastLoop(_cts.Token));
        _ = Task.Run(() => KeepaliveLoop(_cts.Token));
        Console.WriteLine("[P2P:Sync] Overworld state sync started (20Hz broadcast + keepalives)");
    }

    /// <summary>
    /// Stop the broadcast loop.
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        _broadcastTask?.Wait(TimeSpan.FromSeconds(2));
        Console.WriteLine("[P2P:Sync] Overworld state sync stopped");
    }

    /// <summary>
    /// Broadcast loop: sends local state to all peers at 20Hz when dirty.
    /// </summary>
    private async Task BroadcastLoop(CancellationToken ct)
    {
        const int tickIntervalMs = 50; // 20Hz

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(tickIntervalMs, ct);

                // Only broadcast when state has changed
                if (!_localDirty) continue;
                _localDirty = false;

                var stateUpdate = new PeerMessage
                {
                    Type = PeerMessageTypes.StateUpdate,
                    StateUpdate = new PeerStateUpdatePayload
                    {
                        PeerId = _localIdentity.PeerId,
                        DisplayName = _localIdentity.DisplayName,
                        X = _localX,
                        Y = _localY,
                        VelocityX = _localVelocityX,
                        VelocityY = _localVelocityY,
                        Status = _localStatus,
                        PartyId = _localPartyId,
                        IsPartyLeader = _localIsPartyLeader,
                        Figure = PeerIdentity.NormalizeFigure(_localIdentity.Figure),
                        Relocate = _localRelocate,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }
                };
                _localRelocate = false;

                await _mesh.BroadcastAsync(stateUpdate);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:Sync] Broadcast error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Periodic keepalives so LatencyMs is real and dead peers are pruned.
    /// </summary>
    private async Task KeepaliveLoop(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(PeerProtocol.KeepaliveIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                await _mesh.SendKeepalivesAsync();
                await _mesh.CheckConnectionHealthAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:Sync] Keepalive error: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // RECEIVING REMOTE STATE
    // =========================================================================

    /// <summary>
    /// Handle incoming peer messages — route state updates to the remote player map.
    /// </summary>
    private void HandlePeerMessage(PeerConnection connection, PeerMessage message)
    {
        switch (message.Type)
        {
            case PeerMessageTypes.StateUpdate when message.StateUpdate != null:
                ApplyRemoteState(message.StateUpdate);
                break;

            case PeerMessageTypes.ChatRelay when message.ChatRelay != null:
                HandleIncomingChat(message.ChatRelay);
                break;

            case PeerMessageTypes.AdminBroadcast when message.AdminBroadcast != null:
                HandleIncomingAdmin(message.AdminBroadcast);
                break;

            // Keepalive handling
            case PeerMessageTypes.Keepalive when message.Keepalive != null:
                HandleKeepalive(connection, message.Keepalive);
                break;

            case PeerMessageTypes.KeepaliveAck when message.KeepaliveAck != null:
                HandleKeepaliveAck(connection, message.KeepaliveAck);
                break;
        }
    }

    /// <summary>
    /// Apply a received state update from a remote peer.
    /// Validates the update via anti-cheat before applying.
    /// </summary>
    private void ApplyRemoteState(PeerStateUpdatePayload state)
    {
        // Run anti-cheat validation (if validator is set)
        if (_validator != null)
        {
            var isValid = _validator.ValidateStateUpdate(state);
            if (!isValid)
            {
                // Still apply (so we can track their claimed position for future validation)
                // but the validator handles reporting internally
            }
        }

        _remotePlayers.AddOrUpdate(
            state.PeerId,
            // Add new remote player
            _ => new RemotePlayerState
            {
                PeerId = state.PeerId,
                DisplayName = state.DisplayName,
                X = state.X,
                Y = state.Y,
                VelocityX = state.VelocityX,
                VelocityY = state.VelocityY,
                Status = state.Status,
                PartyId = state.PartyId,
                IsPartyLeader = state.IsPartyLeader,
                Figure = PeerIdentity.NormalizeFigure(state.Figure),
                LastUpdated = DateTime.UtcNow,
                RemoteTimestamp = state.Timestamp,
            },
            // Update existing remote player
            (_, existing) =>
            {
                existing.DisplayName = state.DisplayName;
                existing.X = state.X;
                existing.Y = state.Y;
                existing.VelocityX = state.VelocityX;
                existing.VelocityY = state.VelocityY;
                existing.Status = state.Status;
                existing.PartyId = state.PartyId;
                existing.IsPartyLeader = state.IsPartyLeader;
                existing.Figure = PeerIdentity.NormalizeFigure(state.Figure);
                existing.LastUpdated = DateTime.UtcNow;
                existing.RemoteTimestamp = state.Timestamp;
                return existing;
            });
    }

    /// <summary>
    /// Handle a peer leaving the mesh — remove their player from our view.
    /// </summary>
    private void HandlePeerLeft(PeerConnection connection)
    {
        if (!string.IsNullOrEmpty(connection.RemotePeerId))
        {
            _remotePlayers.TryRemove(connection.RemotePeerId, out _);
        }
    }

    /// <summary>
    /// Handle a peer joining the mesh — send immediate initial state so they know about us.
    /// </summary>
    private void HandlePeerJoined(PeerConnection connection)
    {
        // Send an immediate state update to the newly joined peer, even if we haven't moved.
        // This ensures that when two peers connect via glyph, each peer immediately knows
        // about the other's initial position instead of waiting for movement.
        _ = Task.Run(async () =>
        {
            try
            {
                var initialState = new PeerMessage
                {
                    Type = PeerMessageTypes.StateUpdate,
                    StateUpdate = new PeerStateUpdatePayload
                    {
                        PeerId = _localIdentity.PeerId,
                        DisplayName = _localIdentity.DisplayName,
                        X = _localX,
                        Y = _localY,
                        VelocityX = _localVelocityX,
                        VelocityY = _localVelocityY,
                        Status = _localStatus,
                        PartyId = _localPartyId,
                        IsPartyLeader = _localIsPartyLeader,
                        Figure = PeerIdentity.NormalizeFigure(_localIdentity.Figure),
                        Relocate = true,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }
                };

                await connection.SendAsync(initialState);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:Sync] Failed to send initial state to peer {connection.RemotePeerId}: {ex.Message}");
            }
        });
    }

    // =========================================================================
    // KEEPALIVE HANDLING
    // =========================================================================

    /// <summary>
    /// Respond to a keepalive ping from a remote peer.
    /// </summary>
    private void HandleKeepalive(PeerConnection connection, PeerKeepalivePayload keepalive)
    {
        var ack = new PeerMessage
        {
            Type = PeerMessageTypes.KeepaliveAck,
            KeepaliveAck = new PeerKeepaliveAckPayload
            {
                Sequence = keepalive.Sequence,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        };
        _ = connection.SendAsync(ack);
    }

    /// <summary>
    /// Process a keepalive acknowledgment — update latency measurement.
    /// </summary>
    private void HandleKeepaliveAck(PeerConnection connection, PeerKeepaliveAckPayload ack)
    {
        var rtt = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ack.Sequence);
        connection.UpdateLatency(rtt);
    }

    // =========================================================================
    // QUERY METHODS (for frontend/API)
    // =========================================================================

    /// <summary>
    /// Get all players (local + remote) as a unified list for the frontend.
    /// This is what the local client renders.
    /// </summary>
    public List<RemotePlayerState> GetAllVisiblePlayers()
    {
        var players = new List<RemotePlayerState>();

        // Add local player
        players.Add(new RemotePlayerState
        {
            PeerId = _localIdentity.PeerId,
            DisplayName = _localIdentity.DisplayName,
            X = _localX,
            Y = _localY,
            VelocityX = _localVelocityX,
            VelocityY = _localVelocityY,
            Status = _localStatus,
            PartyId = _localPartyId,
            IsPartyLeader = _localIsPartyLeader,
            Figure = PeerIdentity.NormalizeFigure(_localIdentity.Figure),
            LastUpdated = DateTime.UtcNow,
        });

        // Add remote players (excluding those in dungeons)
        foreach (var remote in _remotePlayers.Values)
        {
            if (remote.Status != "in_dungeon")
            {
                players.Add(remote);
            }
        }

        return players;
    }

    /// <summary>
    /// Get a specific remote player by peer ID.
    /// </summary>
    public RemotePlayerState? GetRemotePlayer(string peerId)
    {
        _remotePlayers.TryGetValue(peerId, out var player);
        return player;
    }

    /// <summary>
    /// Remove stale remote players (not updated in a long time).
    /// Called periodically by the mesh health check.
    /// </summary>
    public void PruneStaleRemotePlayers(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var staleIds = _remotePlayers
            .Where(kv => kv.Value.LastUpdated < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in staleIds)
        {
            _remotePlayers.TryRemove(id, out _);
            Console.WriteLine($"[P2P:Sync] Pruned stale remote player: {id}");
        }
    }

    // =========================================================================
    // CHAT SYSTEM
    // =========================================================================

    /// <summary>
    /// Send a chat message to all mesh peers (called by the local frontend).
    /// </summary>
    public async Task SendChatAsync(string channel, string text)
    {
        var messageId = Guid.NewGuid().ToString("N")[..8];

        var chatRelay = new PeerMessage
        {
            Type = PeerMessageTypes.ChatRelay,
            ChatRelay = new PeerChatRelayPayload
            {
                MessageId = messageId,
                Channel = channel,
                SenderId = _localIdentity.PeerId,
                SenderName = _localIdentity.DisplayName,
                Text = text,
                SenderX = _localX,
                SenderY = _localY,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        };

        // Broadcast to all peers
        await _mesh.BroadcastAsync(chatRelay);

        // Also store locally so the sender sees their own message
        StoreChat(new ChatMessageEntry
        {
            MessageId = messageId,
            Channel = channel,
            SenderId = _localIdentity.PeerId,
            SenderName = _localIdentity.DisplayName,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    /// <summary>
    /// Handle a chat message received from a remote peer.
    /// </summary>
    private void HandleIncomingChat(PeerChatRelayPayload chat)
    {
        // Check for "nearby" channel — only store if sender is within range
        if (chat.Channel == "nearby")
        {
            var dx = chat.SenderX - _localX;
            var dy = chat.SenderY - _localY;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > 15f) return; // Too far for nearby chat
        }

        StoreChat(new ChatMessageEntry
        {
            MessageId = chat.MessageId,
            Channel = chat.Channel,
            SenderId = chat.SenderId,
            SenderName = chat.SenderName,
            Text = chat.Text,
            Timestamp = chat.Timestamp,
        });
    }

    /// <summary>
    /// Store a chat message in the buffer (for frontend polling).
    /// </summary>
    private void StoreChat(ChatMessageEntry entry)
    {
        lock (_chatLock)
        {
            _chatMessages.Add(entry);
            // Keep only last 100 messages
            if (_chatMessages.Count > 100)
                _chatMessages.RemoveRange(0, _chatMessages.Count - 100);
        }
    }

    /// <summary>
    /// Get recent chat messages (for frontend polling).
    /// </summary>
    /// <param name="sinceTimestamp">Only return messages after this timestamp (0 for all).</param>
    public List<ChatMessageEntry> GetRecentChat(long sinceTimestamp = 0)
    {
        lock (_chatLock)
        {
            if (sinceTimestamp == 0)
                return _chatMessages.TakeLast(50).ToList();
            return _chatMessages.Where(m => m.Timestamp > sinceTimestamp).ToList();
        }
    }

    // =========================================================================
    // ADMIN MESSAGES
    // =========================================================================

    /// <summary>
    /// Handle an admin broadcast received from a peer (relayed from tracker).
    /// </summary>
    private void HandleIncomingAdmin(PeerAdminBroadcastPayload admin)
    {
        StoreAdmin(new AdminMessageEntry
        {
            MessageId = admin.MessageId,
            Message = admin.Message,
            Priority = admin.Priority,
            DurationSeconds = admin.DurationSeconds,
            Timestamp = admin.Timestamp,
        });
    }

    /// <summary>
    /// Store an admin message from the TrackerClient directly.
    /// </summary>
    public void AddAdminMessage(string messageId, string message, string priority, int duration, long timestamp)
    {
        StoreAdmin(new AdminMessageEntry
        {
            MessageId = messageId,
            Message = message,
            Priority = priority,
            DurationSeconds = duration,
            Timestamp = timestamp,
        });
    }

    private void StoreAdmin(AdminMessageEntry entry)
    {
        lock (_adminLock)
        {
            _adminMessages.Add(entry);
            if (_adminMessages.Count > 20)
                _adminMessages.RemoveRange(0, _adminMessages.Count - 20);
        }
    }

    /// <summary>
    /// Get recent admin messages (for frontend display).
    /// </summary>
    public List<AdminMessageEntry> GetAdminMessages()
    {
        lock (_adminLock)
        {
            return _adminMessages.ToList();
        }
    }
}

// =============================================================================
// CHAT & ADMIN MESSAGE ENTRY TYPES
// =============================================================================

/// <summary>A stored chat message for frontend polling.</summary>
public sealed class ChatMessageEntry
{
    public required string MessageId { get; init; }
    public required string Channel { get; init; }
    public required string SenderId { get; init; }
    public required string SenderName { get; init; }
    public required string Text { get; init; }
    public long Timestamp { get; set; }
}

/// <summary>A stored admin broadcast message for frontend display.</summary>
public sealed class AdminMessageEntry
{
    public required string MessageId { get; init; }
    public required string Message { get; init; }
    public string Priority { get; set; } = "info";
    public int DurationSeconds { get; set; } = 15;
    public long Timestamp { get; set; }
}
