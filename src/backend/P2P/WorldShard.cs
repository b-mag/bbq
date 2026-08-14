// =============================================================================
// WorldShard.cs — World Sharding with 100-Player Cap
// =============================================================================
//
// OVERVIEW:
// The Carcosa overworld is sharded into multiple parallel instances ("worlds"),
// each with a maximum of 100 concurrent players. All worlds share the same
// persistent map (same geography, landmarks, dungeon entrances) but have
// independent player populations.
//
// HOW SHARDING WORKS:
//   - Each world shard has a unique ID (e.g., "carcosa-01", "carcosa-02")
//   - When a new player joins and the current world is full (100 peers),
//     they are redirected to the next available shard
//   - Players can switch shards manually via Glyph codes
//   - The tracker knows which shards exist and their populations
//
// SHARD NAMING:
// Shard IDs are deterministic and human-readable:
//   "carcosa-01", "carcosa-02", ..., "carcosa-FF" (up to 256 shards)
// The naming uses a base prefix + 2-digit hex index.
//
// SHARD SELECTION STRATEGY:
//   1. New player connects → checks their configured world ID
//   2. If no world configured → asks tracker for least-full world
//   3. If no tracker → joins shard "carcosa-01" (default)
//   4. During handshake, if remote peer reports world_full → try next shard
//
// CAPACITY ENFORCEMENT:
// Capacity is enforced at two levels:
//   a. HANDSHAKE: The PeerHandshake validation rejects peers when at capacity
//   b. TRACKER: The tracker won't direct new peers to full worlds
//
// SWITCHING SHARDS:
// To switch shards, a player:
//   1. Disconnects from all current mesh peers
//   2. Updates their local world ID
//   3. Reconnects (via tracker or Glyph) to the new shard's peers
// This is seamless to the frontend — it looks like a brief loading screen.
//
// MAP SHARING:
// All shards use the same overworld.json map. The map file is bundled with
// the game executable. World objects, landmarks, and dungeon entrances are
// identical across all shards.
// =============================================================================

namespace Carcosa.Server.P2P;

/// <summary>
/// Manages world shard assignment, capacity checking, and shard switching.
/// </summary>
public sealed class WorldShard
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>Prefix for all shard IDs.</summary>
    private const string ShardPrefix = "carcosa";

    /// <summary>Maximum number of shards supported (256).</summary>
    private const int MaxShards = 256;

    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly PeerIdentity _localIdentity;
    private readonly PeerMesh _mesh;

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>The current world shard this peer is in.</summary>
    public string CurrentShardId => _localIdentity.WorldId;

    /// <summary>The shard index (0-255) extracted from the shard ID.</summary>
    public byte CurrentShardIndex => ParseShardIndex(_localIdentity.WorldId);

    /// <summary>Whether the current shard is at capacity.</summary>
    public bool IsAtCapacity => _mesh.PeerCount + 1 >= PeerProtocol.MaxPeersPerWorld;

    /// <summary>Current player count in this shard (us + peers).</summary>
    public int PlayerCount => _mesh.PeerCount + 1;

    // =========================================================================
    // CONSTRUCTOR
    // =========================================================================

    public WorldShard(PeerIdentity localIdentity, PeerMesh mesh)
    {
        _localIdentity = localIdentity;
        _mesh = mesh;

        // Assign default shard if none set
        if (string.IsNullOrEmpty(_localIdentity.WorldId))
        {
            _localIdentity.WorldId = GenerateShardId(0);
            Console.WriteLine($"[P2P:Shard] Assigned to default shard: {_localIdentity.WorldId}");
        }
    }

    // =========================================================================
    // SHARD ID GENERATION
    // =========================================================================

    /// <summary>
    /// Generate a deterministic shard ID from an index.
    /// Format: "carcosa-XX" where XX is a 2-digit hex index (00-FF).
    /// </summary>
    /// <param name="index">Shard index (0-255).</param>
    /// <returns>Shard ID string.</returns>
    public static string GenerateShardId(byte index)
    {
        return $"{ShardPrefix}-{index:x2}";
    }

    /// <summary>
    /// Parse a shard index from a shard ID string.
    /// Returns 0 if the ID is invalid.
    /// </summary>
    public static byte ParseShardIndex(string shardId)
    {
        if (string.IsNullOrEmpty(shardId)) return 0;

        var parts = shardId.Split('-');
        if (parts.Length < 2) return 0;

        if (byte.TryParse(parts[^1], System.Globalization.NumberStyles.HexNumber, null, out var index))
            return index;

        return 0;
    }

    // =========================================================================
    // SHARD SELECTION
    // =========================================================================

    /// <summary>
    /// Get the next shard ID to try when the current one is full.
    /// Prefers sequentially increasing shard IDs and expands to a new shard
    /// only when all known shards are already at capacity.
    /// </summary>
    public string GetNextShardId()
    {
        return GetNextAvailableShardId(_localIdentity.WorldId, null);
    }

    /// <summary>
    /// Evaluate the next available shard, preferring the current sequence order
    /// over a global least-full heuristic. This keeps the shard mesh compact while
    /// still allowing overflow into the next shard when needed.
    /// </summary>
    public static string GetNextAvailableShardId(string currentShardId, Dictionary<string, int>? shardPopulations)
    {
        var currentIndex = ParseShardIndex(currentShardId);
        var maxIndex = shardPopulations is not null && shardPopulations.Count > 0
            ? shardPopulations.Keys.Select(ParseShardIndex).DefaultIfEmpty((byte)0).Max()
            : currentIndex;

        // First, prefer the next shard in sequence if it has room.
        for (var i = 1; i <= MaxShards; i++)
        {
            var candidateIndex = (byte)((currentIndex + i) % MaxShards);
            var candidateId = GenerateShardId(candidateIndex);

            if (shardPopulations is null || !shardPopulations.TryGetValue(candidateId, out var count) || count < PeerProtocol.MaxPeersPerWorld)
            {
                return candidateId;
            }
        }

        // If all known shards are full, create the next sequential shard.
        return GenerateShardId((byte)((maxIndex + 1) % MaxShards));
    }

    /// <summary>
    /// Select the best shard for a new player (used by tracker).
    /// Strategy: prefer the fullest shard that still has room, with a fallback to
    /// the next sequential shard if all known shards are at capacity.
    /// </summary>
    /// <param name="shardPopulations">Map of shardId → player count (from tracker).</param>
    /// <returns>The recommended shard ID.</returns>
    public static string SelectBestShard(Dictionary<string, int>? shardPopulations)
    {
        if (shardPopulations == null || shardPopulations.Count == 0)
            return GenerateShardId(0); // Default to first shard

        var available = shardPopulations
            .Where(kv => kv.Value < PeerProtocol.MaxPeersPerWorld)
            .OrderByDescending(kv => kv.Value)
            .ToList();

        if (available.Count > 0)
            return available[0].Key;

        var maxIndex = shardPopulations.Keys
            .Select(ParseShardIndex)
            .DefaultIfEmpty((byte)0)
            .Max();

        return GenerateShardId((byte)(maxIndex + 1));
    }

    /// <summary>
    /// Switch this peer to a different shard. This involves:
    /// 1. Updating the local world ID
    /// 2. Disconnecting from all current peers (they're in the old shard)
    /// 3. The caller should then reconnect via tracker/PEX/Glyph
    /// </summary>
    /// <param name="newShardId">The shard to switch to.</param>
    public async Task SwitchShardAsync(string newShardId)
    {
        if (newShardId == _localIdentity.WorldId)
            return; // Already in this shard

        Console.WriteLine($"[P2P:Shard] Switching from {_localIdentity.WorldId} to {newShardId}");

        // Disconnect from all current peers
        await _mesh.ShutdownAsync();

        // Update our world ID
        _localIdentity.WorldId = newShardId;

        Console.WriteLine($"[P2P:Shard] Now in shard: {newShardId}. Awaiting new connections...");
        // The TrackerClient and PeerExchange will handle reconnection to the new shard
    }

    /// <summary>
    /// Check if a connecting peer should be redirected to another shard (we're full).
    /// Returns the suggested shard ID, or null if we have room.
    /// </summary>
    public string? CheckCapacityRedirect()
    {
        if (!IsAtCapacity) return null;
        return GetNextShardId();
    }

    // =========================================================================
    // INFO
    // =========================================================================

    /// <summary>
    /// Get shard info for display/API. Returns a tuple of values
    /// (the API endpoint creates the typed response record from these).
    /// </summary>
    public (string shardId, byte shardIndex, int playerCount, int maxPlayers, bool isAtCapacity) GetShardInfo()
    {
        return (CurrentShardId, CurrentShardIndex, PlayerCount, PeerProtocol.MaxPeersPerWorld, IsAtCapacity);
    }
}
