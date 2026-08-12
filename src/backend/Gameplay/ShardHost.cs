// =============================================================================
// ShardHost.cs — Deterministic Shard Host Election for P2P Authority
// =============================================================================
//
// OVERVIEW:
// In the fully P2P architecture, one peer in the mesh acts as the "shard host" —
// the authority for enemy AI, combat resolution, and loot drops. Other peers
// send combat actions to the host, who processes them and broadcasts results.
//
// WHY DETERMINISTIC:
// Using the lowest sorted peer ID as host means ALL peers agree on who's host
// without any negotiation protocol. When a peer joins or leaves, all peers
// independently recalculate and arrive at the same answer. No election rounds,
// no voting, no split-brain scenarios.
//
// HOST RESPONSIBILITIES:
//   1. Run enemy AI (Gronk wandering, aggro, attacks)
//   2. Process combat actions from all peers (damage resolution)
//   3. Broadcast enemy state to all peers at 10Hz
//   4. Broadcast damage events to all peers for visual feedback
//   5. Generate and distribute loot drops
//
// HOST MIGRATION:
// When the current host disconnects, the next-lowest peer ID automatically
// becomes host. The new host spawns fresh enemies (state isn't transferred).
// This is acceptable because:
//   - Gronks are ambient — respawning them is cheap and fast
//   - Combat state (HP, tags) resets cleanly
//   - Players barely notice a brief enemy respawn during migration
//
// WHEN ALONE:
// A solo player is always the host (they're the only peer in sorted order).
// This means the game works perfectly in single-player — no dependency on
// any external server or other players.
//
// ARCHITECTURE:
// ShardHostManager is NOT static because it needs references to PeerMesh and
// PeerIdentity (runtime state). It's registered as a singleton service and
// injected where needed.
// =============================================================================

using Carcosa.Server.P2P;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Manages shard host election using deterministic peer ID sorting.
/// The peer with the lowest alphabetically-sorted peer ID is always the host.
/// Re-evaluates on every peer join/leave event.
/// </summary>
public sealed class ShardHostManager
{
    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerMesh _mesh;
    private readonly PeerIdentity _localIdentity;
    private bool _isLocalHost;
    private string? _currentHostId;
    private readonly object _lock = new();

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>
    /// True if this local peer is the current shard host (lowest sorted peer ID).
    /// Used by combat sync to decide whether to process combat actions or forward them.
    /// Thread-safe (read via volatile semantics under lock).
    /// </summary>
    public bool IsLocalHost
    {
        get { lock (_lock) { return _isLocalHost; } }
    }

    /// <summary>
    /// The peer ID of the current shard host. May be our own ID or a remote peer's.
    /// Null only if no peers exist (shouldn't happen — we're always in the mesh).
    /// </summary>
    public string? CurrentHostId
    {
        get { lock (_lock) { return _currentHostId; } }
    }

    // =========================================================================
    // EVENTS
    // =========================================================================

    /// <summary>
    /// Fired when host status changes — either we became host, or we stopped being host.
    /// The bool parameter is the new IsLocalHost value.
    /// Used by EnemySpawner to start/stop enemy AI processing.
    /// </summary>
    public event Action<bool>? OnHostStatusChanged;

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    /// <summary>
    /// Create a shard host manager and immediately subscribe to mesh events.
    /// Calculates initial host status based on current mesh state.
    /// </summary>
    /// <param name="mesh">The P2P mesh to monitor for peer joins/leaves.</param>
    /// <param name="localIdentity">Our local peer identity (for ID comparison).</param>
    public ShardHostManager(PeerMesh mesh, PeerIdentity localIdentity)
    {
        _mesh = mesh;
        _localIdentity = localIdentity;

        // Subscribe to mesh events for re-election
        _mesh.OnPeerJoined += HandlePeerJoined;
        _mesh.OnPeerLeft += HandlePeerLeft;

        // Calculate initial host status (we might be alone = host)
        Recalculate();
    }

    // =========================================================================
    // HOST ELECTION LOGIC
    // =========================================================================

    /// <summary>
    /// Determine which peer should be host from a set of peer IDs.
    /// Uses simple alphabetical sort — lowest string value wins.
    /// 
    /// WHY ALPHABETICAL: Peer IDs are 16-char hex strings generated from GUIDs.
    /// Alphabetical sort on hex strings is stable, deterministic, and produces
    /// the same result on all peers without communication. StringComparison.Ordinal
    /// ensures consistent ordering regardless of locale.
    /// </summary>
    /// <param name="allPeerIds">All peer IDs in the mesh (including self).</param>
    /// <returns>The peer ID that should be host, or null if the collection is empty.</returns>
    public static string? DetermineHost(IEnumerable<string> allPeerIds)
    {
        string? lowest = null;

        foreach (var id in allPeerIds)
        {
            if (lowest == null || string.Compare(id, lowest, StringComparison.Ordinal) < 0)
            {
                lowest = id;
            }
        }

        return lowest;
    }

    /// <summary>
    /// Recalculate host status based on current mesh state.
    /// Called on initialization, peer join, and peer leave.
    /// Fires OnHostStatusChanged if our status changed.
    /// </summary>
    private void Recalculate()
    {
        bool wasHost;
        bool isHostNow;

        lock (_lock)
        {
            wasHost = _isLocalHost;

            // Gather all peer IDs: self + all connected remotes
            var allIds = GetAllPeerIds();
            _currentHostId = DetermineHost(allIds);
            _isLocalHost = _currentHostId == _localIdentity.PeerId;
            isHostNow = _isLocalHost;
        }

        // Fire event outside lock to prevent deadlocks in subscribers
        if (wasHost != isHostNow)
        {
            Console.WriteLine($"[ShardHost] Host status changed: IsHost={isHostNow}, HostId={_currentHostId}");
            OnHostStatusChanged?.Invoke(isHostNow);
        }
    }

    /// <summary>
    /// Get all peer IDs in the mesh (local + all connected remotes).
    /// This is the "voter set" for host election.
    /// </summary>
    public IEnumerable<string> GetAllPeerIds()
    {
        // Start with our own ID
        yield return _localIdentity.PeerId;

        // Add all connected remote peer IDs
        foreach (var peerId in _mesh.ConnectedPeerIds)
        {
            yield return peerId;
        }
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================

    /// <summary>
    /// A new peer joined the mesh — recalculate host (they might have a lower ID).
    /// </summary>
    private void HandlePeerJoined(PeerConnection connection)
    {
        Console.WriteLine($"[ShardHost] Peer joined: {connection.RemotePeerId}. Recalculating host...");
        Recalculate();
    }

    /// <summary>
    /// A peer left the mesh — recalculate host (if they were host, we might become host).
    /// </summary>
    private void HandlePeerLeft(PeerConnection connection)
    {
        Console.WriteLine($"[ShardHost] Peer left: {connection.RemotePeerId}. Recalculating host...");
        Recalculate();
    }
}
