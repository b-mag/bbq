// =============================================================================
// TrackerClient.cs — Optional Peer Discovery via Matchmaking Tracker
// =============================================================================
//
// OVERVIEW:
// The tracker (matchmaking service) provides an optional Layer 1 discovery
// mechanism. When available, it helps peers find each other quickly. When
// unavailable, peers fall back to cached addresses, Glyphs, or LAN broadcast.
//
// HOW IT WORKS:
//   1. On startup, the game server registers itself with the tracker
//      (POST /api/tracker/register with our peer ID, address, and world)
//   2. The tracker returns a list of other peers in the same world
//   3. We connect to those peers to join the mesh
//   4. Periodically (every 30s), we re-register as a heartbeat
//   5. If the tracker goes down, we continue operating via PEX and cache
//
// GRACEFUL DEGRADATION:
// The TrackerClient is entirely non-blocking. If the tracker is unreachable:
//   - Registration silently fails (logged but no error thrown)
//   - Discovery returns empty list (triggers PEX/cache fallback)
//   - The game continues without any interruption
//   - When the tracker comes back, the next heartbeat re-registers us
//
// STUN-LIKE FUNCTIONALITY:
// The tracker also exposes GET /api/tracker/reflect which returns the caller's
// public IP and port as seen by the server. This helps peers discover their
// own public address (for NAT traversal and Glyph generation).
//
// WHY NOT A PERSISTENT WEBSOCKET TO TRACKER:
// HTTP polling is simpler, stateless, and more resilient. If the tracker
// restarts, the next poll automatically re-establishes presence. A WebSocket
// would require reconnection logic and adds complexity for no real benefit
// (peer discovery doesn't need sub-second latency).
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.P2P;

/// <summary>
/// Client that communicates with the optional matchmaking tracker for
/// peer discovery and public address reflection.
/// </summary>
public sealed class TrackerClient
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly HttpClient _http;
    private readonly PeerIdentity _localIdentity;
    private readonly PeerMesh _mesh;
    private readonly WorldShard _worldShard;
    private readonly string _trackerUrl;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _seenAdminMessages = new(); // Deduplication
    private Task? _heartbeatTask;
    private bool _isOnline;

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>Whether the tracker is currently reachable.</summary>
    public bool IsTrackerOnline => _isOnline;

    /// <summary>When we last successfully communicated with the tracker.</summary>
    public DateTime LastContact { get; private set; } = DateTime.MinValue;

    // =========================================================================
    // EVENTS
    // =========================================================================

    /// <summary>Fired when an admin broadcast message is received from the tracker.</summary>
    public event Action<TrackerAdminMessage>? OnAdminMessage;

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    /// <summary>
    /// Create a tracker client.
    /// </summary>
    /// <param name="trackerUrl">Base URL of the matchmaking/tracker service (e.g., "http://localhost:5100").</param>
    /// <param name="localIdentity">Our peer identity.</param>
    /// <param name="mesh">The peer mesh (to connect to discovered peers).</param>
    public TrackerClient(string trackerUrl, PeerIdentity localIdentity, PeerMesh mesh, WorldShard worldShard)
    {
        _trackerUrl = trackerUrl.TrimEnd('/');
        _localIdentity = localIdentity;
        _mesh = mesh;
        _worldShard = worldShard;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Start the tracker client: discover public address, register, and begin heartbeat loop.
    /// All operations are best-effort — failures don't block the game.
    /// </summary>
    public void Start()
    {
        _heartbeatTask = Task.Run(() => HeartbeatLoop(_cts.Token));
        Console.WriteLine($"[P2P:Tracker] Started (URL: {_trackerUrl})");
        if (_localIdentity.PublicAddressPinned)
            Console.WriteLine($"[P2P:Tracker] Public address pinned at {_localIdentity.PublicAddress}; reflect skipped");
    }

    /// <summary>
    /// Stop the heartbeat loop and deregister from the tracker.
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
        _ = DeregisterAsync();
        Console.WriteLine("[P2P:Tracker] Stopped");
    }

    // =========================================================================
    // HEARTBEAT LOOP
    // =========================================================================

    /// <summary>
    /// Periodic registration loop: register with tracker and discover peers.
    /// Runs every 30 seconds. Resilient to tracker outages.
    /// </summary>
    private async Task HeartbeatLoop(CancellationToken ct)
    {
        // Initial delay to let the server start
        await Task.Delay(3000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Step 1: Discover our public address (STUN-like) unless --public-address pinned it
                if (!_localIdentity.PublicAddressPinned)
                    await DiscoverPublicAddressAsync();

                // Step 2: Register with tracker
                var peers = await RegisterAndDiscoverAsync();

                // Step 3: Connect to discovered peers
                if (peers != null)
                {
                    if (peers.Length > 0)
                    {
                        Console.WriteLine($"[P2P:Tracker] Discovered {peers.Length} peer(s) from tracker");
                    }
                    foreach (var peer in peers)
                    {
                        if (peer.PeerId == _localIdentity.PeerId) continue;
                        if (_mesh.IsPeerConnected(peer.PeerId)) continue;
                        if (string.IsNullOrEmpty(peer.Address)) continue;
                        if (_mesh.PeerCount >= PeerProtocol.MaxPeersPerWorld) break;

                        Console.WriteLine($"[P2P:Tracker] Connecting to discovered peer: {peer.DisplayName} at {peer.Address}");
                        _ = _mesh.ConnectToPeerAsync(peer.Address);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (_isOnline)
                {
                    Console.WriteLine($"[P2P:Tracker] Lost connection: {ex.Message}");
                    _isOnline = false;
                }
            }

            // Wait before next heartbeat
            try { await Task.Delay(30000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // =========================================================================
    // TRACKER API CALLS
    // =========================================================================

    /// <summary>
    /// Discover our public IP:port via the tracker's reflect endpoint.
    /// This is a STUN-like service that tells us how we appear from the outside.
    /// Falls back to localhost:PORT if reflection fails (common in development).
    /// </summary>
    private async Task DiscoverPublicAddressAsync()
    {
        if (_localIdentity.PublicAddressPinned)
            return;

        try
        {
            var response = await _http.GetAsync($"{_trackerUrl}/api/tracker/reflect");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var reflect = JsonSerializer.Deserialize(json, TrackerJsonContext.Default.TrackerReflectResponse);
                if (reflect != null && !string.IsNullOrEmpty(reflect.Address))
                {
                    var ip = reflect.Address;
                    if (PeerAddress.IsLoopbackHost(ip))
                    {
                        // Tracker saw loopback (common when the tracker is on this machine).
                        // Keep STUN/UPnP discovery so glyphs do not regress to 127.0.0.1.
                        if (!string.IsNullOrEmpty(_localIdentity.PublicAddress) &&
                            !PeerAddress.IsLoopbackAddress(_localIdentity.PublicAddress))
                        {
                            return;
                        }

                        ip = "127.0.0.1";
                    }

                    var candidate = PeerAddress.Compose(ip, _localIdentity.ListenPort);
                    if (!string.Equals(_localIdentity.PublicAddress, candidate, StringComparison.Ordinal))
                        Console.WriteLine($"[P2P:Tracker] Reflected address: {candidate}");
                    _localIdentity.PublicAddress = candidate;
                    return;
                }
            }
        }
        catch { /* Fall through to default */ }

        if (string.IsNullOrEmpty(_localIdentity.PublicAddress))
        {
            _localIdentity.PublicAddress = PeerAddress.Compose("127.0.0.1", _localIdentity.ListenPort);
        }
    }

    /// <summary>
    /// Register ourselves with the tracker and receive back the list of peers in our world.
    /// Also receives any pending admin messages to relay to the local frontend.
    /// </summary>
    private async Task<TrackerPeerInfo[]?> RegisterAndDiscoverAsync()
    {
        try
        {
            var registration = new TrackerRegistration
            {
                PeerId = _localIdentity.PeerId,
                DisplayName = _localIdentity.DisplayName,
                Address = _localIdentity.PublicAddress,
                WorldId = _localIdentity.WorldId,
                PlayerCount = _mesh.PeerCount + 1, // +1 for ourselves
                GameVersion = PeerProtocol.GameVersionString,
            };

            var json = JsonSerializer.Serialize(registration, TrackerJsonContext.Default.TrackerRegistration);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _http.PostAsync($"{_trackerUrl}/api/tracker/register", content);

            if (response.IsSuccessStatusCode)
            {
                _isOnline = true;
                LastContact = DateTime.UtcNow;

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize(responseJson, TrackerJsonContext.Default.TrackerRegisterResponse);

                if (result?.Peers != null)
                {
                    var shardPopulation = result.Peers
                        .GroupBy(p => p.WorldId)
                        .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                        .ToDictionary(g => g.Key, g => g.Count());

                    if (string.IsNullOrWhiteSpace(_localIdentity.WorldId) ||
                        shardPopulation.TryGetValue(_localIdentity.WorldId, out var localCount) && localCount >= PeerProtocol.MaxPeersPerWorld)
                    {
                        var nextShard = WorldShard.GetNextAvailableShardId(_localIdentity.WorldId, shardPopulation);
                        if (!string.Equals(_localIdentity.WorldId, nextShard, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[P2P:Tracker] Current shard {_localIdentity.WorldId} is full; moving to {nextShard}");
                            await _worldShard.SwitchShardAsync(nextShard);
                            return await RegisterAndDiscoverAsync();
                        }
                    }
                }

                // Process admin messages (relay to mesh)
                if (result?.AdminMessages != null)
                {
                    foreach (var admin in result.AdminMessages)
                    {
                        if (_seenAdminMessages.Contains(admin.MessageId)) continue;
                        _seenAdminMessages.Add(admin.MessageId);

                        // Relay to the mesh as a PeerAdminBroadcast
                        var adminMsg = new PeerMessage
                        {
                            Type = PeerMessageTypes.AdminBroadcast,
                            AdminBroadcast = new PeerAdminBroadcastPayload
                            {
                                Message = admin.Message,
                                Priority = admin.Priority,
                                DurationSeconds = admin.DurationSeconds,
                                Timestamp = admin.Timestamp,
                                MessageId = admin.MessageId,
                            }
                        };
                        await _mesh.BroadcastAsync(adminMsg);

                        // Also notify local listeners
                        OnAdminMessage?.Invoke(admin);

                        Console.WriteLine($"[P2P:Tracker] Admin broadcast: \"{admin.Message}\"");
                    }
                }

                return result?.Peers;
            }
            else
            {
                _isOnline = false;
            }
        }
        catch
        {
            _isOnline = false;
        }

        return null;
    }

    /// <summary>
    /// Deregister from the tracker (called on shutdown).
    /// </summary>
    private async Task DeregisterAsync()
    {
        try
        {
            await _http.DeleteAsync($"{_trackerUrl}/api/tracker/peers/{_localIdentity.PeerId}");
        }
        catch { /* Best effort */ }
    }
}

// =============================================================================
// TRACKER API TYPES (shared between client and server)
// =============================================================================

/// <summary>Registration payload sent to the tracker.</summary>
public sealed class TrackerRegistration
{
    public required string PeerId { get; init; }
    public required string DisplayName { get; init; }
    public required string Address { get; init; }
    public required string WorldId { get; init; }
    public int PlayerCount { get; set; }
    public required string GameVersion { get; init; }
}

/// <summary>Response from the tracker's register endpoint.</summary>
public sealed class TrackerRegisterResponse
{
    public TrackerPeerInfo[]? Peers { get; set; }
    public string? WorldId { get; set; }
    public TrackerAdminMessage[]? AdminMessages { get; set; }
}

/// <summary>A peer entry returned by the tracker.</summary>
public sealed class TrackerPeerInfo
{
    public required string PeerId { get; init; }
    public required string Address { get; init; }
    public string DisplayName { get; set; } = "";
    public string WorldId { get; set; } = "";
}

/// <summary>An admin broadcast message from the tracker.</summary>
public sealed class TrackerAdminMessage
{
    public required string MessageId { get; init; }
    public required string Message { get; init; }
    public string Priority { get; set; } = "info";
    public int DurationSeconds { get; set; } = 15;
    public long Timestamp { get; set; }
}

/// <summary>Response from the tracker's reflect (STUN-like) endpoint.</summary>
public sealed class TrackerReflectResponse
{
    public required string Address { get; init; }
    public int Port { get; set; }
}

/// <summary>AOT JSON context for tracker communication.</summary>
[JsonSerializable(typeof(TrackerRegistration))]
[JsonSerializable(typeof(TrackerRegisterResponse))]
[JsonSerializable(typeof(TrackerPeerInfo))]
[JsonSerializable(typeof(TrackerPeerInfo[]))]
[JsonSerializable(typeof(TrackerAdminMessage))]
[JsonSerializable(typeof(TrackerAdminMessage[]))]
[JsonSerializable(typeof(TrackerReflectResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class TrackerJsonContext : JsonSerializerContext { }
