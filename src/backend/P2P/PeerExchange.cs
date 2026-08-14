// =============================================================================
// PeerExchange.cs — Peer Exchange (PEX) Protocol
// =============================================================================
//
// OVERVIEW:
// Peer Exchange is the mechanism by which the mesh network self-discovers.
// Every PeerExchangeIntervalSeconds (30s), each peer sends its known peer list
// to all connected peers. Recipients connect to any peers they don't already
// know about. This ensures:
//
//   1. FULL MESH CONVERGENCE: Connect to 1 peer → learn about all peers within
//      ~2 exchange cycles (60 seconds worst case).
//
//   2. SELF-HEALING: If a peer temporarily disconnects and reconnects to just
//      one other peer, PEX will re-establish connections to the full mesh.
//
//   3. NEW PEER BOOTSTRAP: A new peer connects to a single known address
//      (from tracker, Glyph, or cache). PEX immediately provides the full
//      peer list so they can join the mesh.
//
// HOW IT WORKS:
//   ┌────────────────────────────────────────────────────────────────────┐
//   │ Every 30 seconds:                                                  │
//   │                                                                    │
//   │  1. Gather our known peers: GetPeerEndpoints() from PeerMesh       │
//   │     (includes ID, address, display name, world shard)              │
//   │                                                                    │
//   │  2. Include our OWN endpoint (so receivers learn our address)       │
//   │                                                                    │
//   │  3. Broadcast PeerExchange message to all connected peers           │
//   │                                                                    │
//   │  4. On RECEIVING a PeerExchange:                                    │
//   │     - For each peer in the list:                                    │
//   │       - Skip if it's us (same peer ID)                              │
//   │       - Skip if already connected                                   │
//   │       - Skip if address is empty                                    │
//   │       - Attempt to connect (ConnectToPeerAsync)                     │
//   └────────────────────────────────────────────────────────────────────┘
//
// PERSISTENCE:
// Known peer addresses are saved to a local cache file (known-peers.json).
// On startup, if no tracker is available and no Glyph is entered, the peer
// tries to reconnect to cached addresses. This provides resilience across
// restarts.
//
// LOOP PREVENTION:
// PEX messages are NOT re-broadcast. Each peer only shares DIRECTLY known
// peers (those it has an active connection to). This prevents exponential
// message amplification.
//
// CAPACITY AWARENESS:
// PEX respects the MaxPeersPerWorld limit. If we're at capacity, we stop
// attempting new outbound connections from PEX data.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carcosa.Server.P2P;

public sealed class PeerExchangeSettings
{
    public bool AllowCacheBootstrap { get; init; } = true;
    public bool ClearCacheOnStartup { get; init; } = false;

    public static PeerExchangeSettings Default { get; } = new();

    public static PeerExchangeSettings FromArgs(string[] args)
    {
        var hasNoCacheConnect = args.Any(a =>
            string.Equals(a, "--no-cache-connect", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--disable-cache-bootstrap", StringComparison.OrdinalIgnoreCase));

        var hasClearCache = args.Any(a =>
            string.Equals(a, "--clear-peer-cache", StringComparison.OrdinalIgnoreCase));

        return new PeerExchangeSettings
        {
            AllowCacheBootstrap = !hasNoCacheConnect,
            ClearCacheOnStartup = hasClearCache,
        };
    }
}

/// <summary>
/// Manages periodic peer exchange (sharing known peer lists) and
/// persists the peer cache to disk for reconnection across restarts.
/// </summary>
public sealed class PeerExchange
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _cacheFilePath;
    private readonly bool _allowCacheBootstrap;
    private readonly bool _clearCacheOnStartup;
    private Task? _exchangeTask;

    /// <summary>
    /// Cached known peer addresses (persisted to disk).
    /// Key: peer ID, Value: last known address.
    /// </summary>
    private Dictionary<string, CachedPeerInfo> _peerCache = new();

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    public PeerExchange(PeerMesh mesh, PeerIdentity localIdentity, PeerExchangeSettings? settings = null)
    {
        settings ??= PeerExchangeSettings.Default;

        _mesh = mesh;
        _localIdentity = localIdentity;
        _cacheFilePath = Path.Combine(AppContext.BaseDirectory, "known-peers.json");
        _allowCacheBootstrap = settings.AllowCacheBootstrap;
        _clearCacheOnStartup = settings.ClearCacheOnStartup;

        if (_clearCacheOnStartup)
        {
            _peerCache.Clear();
            if (File.Exists(_cacheFilePath))
            {
                try { File.Delete(_cacheFilePath); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[P2P:PEX] Failed to clear peer cache: {ex.Message}");
                }
            }
        }

        // Subscribe to incoming PEX messages
        _mesh.OnPeerMessage += HandlePeerMessage;

        // Load cached peers from disk
        LoadCache();
    }

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Start the periodic peer exchange broadcast loop.
    /// </summary>
    public void Start()
    {
        _exchangeTask = Task.Run(() => ExchangeLoop(_cts.Token));
        Console.WriteLine($"[P2P:PEX] Started (interval: {PeerProtocol.PeerExchangeIntervalSeconds}s, " +
            $"cached peers: {_peerCache.Count})");
    }

    /// <summary>
    /// Stop the exchange loop and save the cache.
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        _exchangeTask?.Wait(TimeSpan.FromSeconds(2));
        SaveCache();
        Console.WriteLine("[P2P:PEX] Stopped");
    }

    // =========================================================================
    // EXCHANGE LOOP
    // =========================================================================

    /// <summary>
    /// Periodically broadcast our known peer list to all connected peers.
    /// </summary>
    private async Task ExchangeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PeerProtocol.PeerExchangeIntervalSeconds * 1000, ct);

                if (_mesh.PeerCount == 0) continue;

                // Build peer list: all our connected peers + ourselves
                var endpoints = _mesh.GetPeerEndpoints().ToList();

                // Include our own endpoint so receivers learn our address
                if (!string.IsNullOrEmpty(_localIdentity.PublicAddress))
                {
                    endpoints.Add(new PeerEndpoint
                    {
                        PeerId = _localIdentity.PeerId,
                        Address = _localIdentity.PublicAddress,
                        DisplayName = _localIdentity.DisplayName,
                        WorldId = _localIdentity.WorldId,
                    });
                }

                if (endpoints.Count == 0) continue;

                // Broadcast to all connected peers
                var pexMessage = new PeerMessage
                {
                    Type = PeerMessageTypes.PeerExchange,
                    PeerExchange = new PeerExchangePayload
                    {
                        Peers = endpoints.ToArray()
                    }
                };

                await _mesh.BroadcastAsync(pexMessage);

                // Update our cache with current connections
                UpdateCache();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P:PEX] Exchange loop error: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // INCOMING PEX HANDLING
    // =========================================================================

    /// <summary>
    /// Handle incoming peer messages — process PEX data.
    /// </summary>
    private void HandlePeerMessage(PeerConnection connection, PeerMessage message)
    {
        if (message.Type == PeerMessageTypes.PeerExchange && message.PeerExchange != null)
        {
            ProcessPeerExchange(message.PeerExchange);
        }
    }

    /// <summary>
    /// Process a received peer exchange message. Connect to any unknown peers.
    /// </summary>
    private void ProcessPeerExchange(PeerExchangePayload pex)
    {
        foreach (var endpoint in pex.Peers)
        {
            // Skip ourselves
            if (endpoint.PeerId == _localIdentity.PeerId) continue;

            // Skip if already connected
            if (_mesh.IsPeerConnected(endpoint.PeerId)) continue;

            // Skip empty addresses
            if (string.IsNullOrEmpty(endpoint.Address)) continue;

            // Skip if at capacity
            if (_mesh.PeerCount >= PeerProtocol.MaxPeersPerWorld) break;

            // Cache the peer info (even if we don't connect now)
            _peerCache[endpoint.PeerId] = new CachedPeerInfo
            {
                PeerId = endpoint.PeerId,
                Address = endpoint.Address,
                DisplayName = endpoint.DisplayName,
                WorldId = endpoint.WorldId,
                LastSeen = DateTime.UtcNow,
            };

            // Attempt to connect (fire and forget — ConnectToPeerAsync handles failures)
            Console.WriteLine($"[P2P:PEX] Discovered new peer: {endpoint.DisplayName} " +
                $"({endpoint.PeerId}) at {endpoint.Address}");
            _ = _mesh.ConnectToPeerAsync(endpoint.Address);
        }
    }

    // =========================================================================
    // BOOTSTRAP FROM CACHE
    // =========================================================================

    /// <summary>
    /// Attempt to connect to cached peers from a previous session.
    /// Called on startup when no tracker is available.
    /// </summary>
    public async Task ConnectFromCacheAsync()
    {
        if (!_allowCacheBootstrap)
        {
            Console.WriteLine("[P2P:PEX] Cache bootstrap disabled by startup flag (--no-cache-connect)");
            return;
        }

        if (_peerCache.Count == 0)
        {
            Console.WriteLine("[P2P:PEX] No cached peers available for bootstrap");
            return;
        }

        Console.WriteLine($"[P2P:PEX] Attempting to bootstrap from {_peerCache.Count} cached peer(s)...");

        // Try cached peers, most recently seen first
        var sortedPeers = _peerCache.Values
            .OrderByDescending(p => p.LastSeen)
            .Take(10) // Try at most 10 to avoid flooding
            .ToList();

        foreach (var cached in sortedPeers)
        {
            if (_mesh.PeerCount >= PeerProtocol.MaxPeersPerWorld) break;

            var success = await _mesh.ConnectToPeerAsync(cached.Address);
            if (success)
            {
                Console.WriteLine($"[P2P:PEX] Reconnected to cached peer: {cached.DisplayName} " +
                    $"({cached.Address})");
                // PEX will handle discovering the rest of the mesh
                return; // One successful connection is enough — PEX does the rest
            }
        }

        Console.WriteLine("[P2P:PEX] Could not reconnect to any cached peers");
    }

    // =========================================================================
    // CACHE PERSISTENCE
    // =========================================================================

    /// <summary>
    /// Update the in-memory cache from current mesh connections.
    /// </summary>
    private void UpdateCache()
    {
        foreach (var connection in _mesh.Connections)
        {
            if (string.IsNullOrEmpty(connection.RemotePeerId)) continue;
            if (string.IsNullOrEmpty(connection.RemoteAddress)) continue;

            _peerCache[connection.RemotePeerId] = new CachedPeerInfo
            {
                PeerId = connection.RemotePeerId,
                Address = connection.RemoteAddress,
                DisplayName = connection.RemoteDisplayName,
                WorldId = connection.RemoteWorldId,
                LastSeen = DateTime.UtcNow,
            };
        }

        // Prune very old entries (older than 7 days)
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var staleKeys = _peerCache
            .Where(kv => kv.Value.LastSeen < cutoff)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in staleKeys) _peerCache.Remove(key);

        // Persist to disk
        SaveCache();
    }

    /// <summary>
    /// Load the peer cache from disk.
    /// </summary>
    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return;
            var json = File.ReadAllText(_cacheFilePath);
            var loaded = JsonSerializer.Deserialize(json, PeerCacheJsonContext.Default.DictionaryStringCachedPeerInfo);
            if (loaded != null) _peerCache = loaded;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:PEX] Failed to load peer cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Save the peer cache to disk.
    /// </summary>
    private void SaveCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_peerCache, PeerCacheJsonContext.Default.DictionaryStringCachedPeerInfo);
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[P2P:PEX] Failed to save peer cache: {ex.Message}");
        }
    }

    // =========================================================================
    // PUBLIC QUERIES
    // =========================================================================

    /// <summary>
    /// Get all cached peer addresses (for display/debugging).
    /// </summary>
    public IReadOnlyDictionary<string, CachedPeerInfo> GetCachedPeers() => _peerCache;
}

// =============================================================================
// CACHE DATA TYPES
// =============================================================================

/// <summary>
/// A cached peer entry persisted to disk for reconnection across restarts.
/// </summary>
public sealed class CachedPeerInfo
{
    public required string PeerId { get; init; }
    public required string Address { get; init; }
    public string DisplayName { get; set; } = "";
    public string WorldId { get; set; } = "";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// AOT-compatible JSON context for peer cache persistence.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, CachedPeerInfo>))]
[JsonSerializable(typeof(CachedPeerInfo))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class PeerCacheJsonContext : JsonSerializerContext { }
