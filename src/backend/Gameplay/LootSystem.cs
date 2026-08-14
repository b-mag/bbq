// =============================================================================
// LootSystem.cs — Loot Drop Generation & Eligibility (RuneScape-Style)
// =============================================================================
//
// OVERVIEW:
// Handles loot drop generation when enemies die. Determines what drops and
// who is eligible to pick it up based on RuneScape-style tagging rules.
//
// TAGGING RULES (from design):
//   1. SOLO TAG: First attacker "tags" the enemy. Only they can loot it.
//   2. PARTY TAG: If tagger is in a party, loot rotates among party members
//      using round-robin (ensures fairness over many kills).
//   3. ELITE/BOSS TAG: All party members who dealt damage get individual drops.
//
// LOOT TABLES:
// Each enemy SubType has a loot table — a list of possible drops with
// weighted chances. The system rolls against these weights to determine drops.
//
// GROUND DROPS:
// Loot appears on the ground at the enemy's death position. Only eligible
// players can see/pick it up. Drops despawn after 30 seconds if not collected.
//
// WHY STATIC:
// LootSystem is stateless logic — it takes enemy data and produces drop results.
// Ground drop state is managed by OverworldCombatSync (tracks active drops).
// =============================================================================

using System.Collections.Concurrent;
using Carcosa.Server.P2P;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// A single entry in a loot table: an item that can drop with a given weight.
/// Weight is relative — higher weight = more likely when rolling.
/// </summary>
public sealed record LootTableEntry
{
    /// <summary>Item ID from ItemRegistry.</summary>
    public required string ItemId { get; init; }
    /// <summary>Relative drop weight (higher = more common). Out of total table weight.</summary>
    public required int Weight { get; init; }
    /// <summary>Minimum item quantity dropped (for stackable items).</summary>
    public int MinQuantity { get; init; } = 1;
    /// <summary>Maximum item quantity dropped (for stackable items).</summary>
    public int MaxQuantity { get; init; } = 1;
}

/// <summary>
/// Represents a loot drop on the ground in the overworld.
/// Tracks position, eligibility, and despawn timing.
/// </summary>
public sealed class GroundLootDrop
{
    /// <summary>Unique drop ID.</summary>
    public required string DropId { get; init; }
    /// <summary>Item ID of the dropped item.</summary>
    public required string ItemId { get; init; }
    /// <summary>Quantity of the item (for stackable items).</summary>
    public int Quantity { get; set; } = 1;
    /// <summary>World X position where the loot dropped.</summary>
    public float X { get; set; }
    /// <summary>World Y position.</summary>
    public float Y { get; set; }
    /// <summary>
    /// Peer IDs eligible to pick up this drop. Empty = anyone can pick it up.
    /// Based on RuneScape tagging rules.
    /// </summary>
    public required HashSet<string> EligiblePeerIds { get; init; }

    /// <summary>Server tick when this drop was created (for time-based eligibility).</summary>
    public long CreatedAtServerTick { get; init; }

    /// <summary>Ticks after creation before despawn (default 120s at 20Hz).</summary>
    public int DespawnAfterTicks { get; set; } = DeterministicLootGenerator.DespawnAfterTicks;

    /// <summary>How eligibility evolves over time for this drop.</summary>
    public LootDropMode DropMode { get; set; } = LootDropMode.Solo;

    /// <summary>Deterministic seed used to generate this drop (elite drops).</summary>
    public string? GenerationSeed { get; set; }

    /// <summary>Whether this drop has been collected.</summary>
    public bool IsCollected { get; set; }

    /// <summary>Peer that collected this drop.</summary>
    public string? CollectedByPeerId { get; set; }

    /// <summary>Server tick when collected.</summary>
    public long CollectedAtTick { get; set; }

    /// <summary>Rarity of the dropped item (for rendering glow color on client).</summary>
    public ItemRarity Rarity { get; set; }

    public bool IsExpired(int currentTick)
        => !IsCollected && (currentTick - CreatedAtServerTick) > DespawnAfterTicks;

    public bool CanPickup(string peerId, int currentTick)
        => LootDropVisibility.CanPickup(this, peerId, currentTick);
}

/// <summary>
/// Generates loot drops and manages eligibility rules.
/// </summary>
public static class LootSystem
{
    // =========================================================================
    // LOOT TABLES (per enemy SubType)
    // =========================================================================

    /// <summary>
    /// Gronk loot table: mostly meat and feathers, small chance of a bone charm.
    /// Total weight determines individual drop chances.
    /// </summary>
    private static readonly LootTableEntry[] GronkLootTable =
    [
        new LootTableEntry { ItemId = "raw_gronk_meat", Weight = 70, MinQuantity = 1, MaxQuantity = 2 },
        new LootTableEntry { ItemId = "dark_feathers", Weight = 50, MinQuantity = 1, MaxQuantity = 3 },
        new LootTableEntry { ItemId = "gronk_bone_charm", Weight = 8 },
        new LootTableEntry { ItemId = "gronk_bone_knife", Weight = 5 },
        new LootTableEntry { ItemId = "tattered_hide", Weight = 12 },
        new LootTableEntry { ItemId = "worn_leather_boots", Weight = 6 },
    ];

    // Map of SubType → loot table
    private static readonly Dictionary<string, LootTableEntry[]> LootTables = new()
    {
        ["gronk"] = GronkLootTable,
    };

    /// <summary>Expose loot table for deterministic generation.</summary>
    internal static LootTableEntry[]? GetLootTable(string enemySubType)
    {
        if (LootTables.TryGetValue(enemySubType, out var table))
            return table;

        if (enemySubType.StartsWith("elite_", StringComparison.OrdinalIgnoreCase))
        {
            var baseType = enemySubType["elite_".Length..];
            if (LootTables.TryGetValue(baseType, out table))
                return table;
        }

        return null;
    }

    // Drop ID counter
    private static int _dropCounter;

    // =========================================================================
    // LOOT GENERATION
    // =========================================================================

    /// <summary>
    /// Generate loot drops for a killed enemy. Rolls against the loot table
    /// and returns 0-2 drops (typically 1 guaranteed common + chance for uncommon).
    /// 
    /// Each entry in the table is rolled independently — an enemy can drop
    /// multiple items from the same table.
    /// </summary>
    /// <param name="enemySubType">Enemy SubType (e.g., "gronk") to look up loot table.</param>
    /// <param name="enemyX">X position where drops should appear.</param>
    /// <param name="enemyY">Y position where drops should appear.</param>
    /// <param name="eligiblePeerIds">Set of peer IDs allowed to pick up (from tagging rules).</param>
    /// <returns>List of ground loot drops (may be empty if no table or bad luck).</returns>
    public static List<GroundLootDrop> GenerateDrops(
        string enemySubType, float enemyX, float enemyY, HashSet<string> eligiblePeerIds,
        long createdAtServerTick = 0, LootDropMode mode = LootDropMode.Solo)
    {
        var drops = new List<GroundLootDrop>();

        if (!LootTables.TryGetValue(enemySubType, out var table))
            return drops;

        var rng = Random.Shared;

        // Calculate total weight for probability
        int totalWeight = 0;
        foreach (var entry in table) totalWeight += entry.Weight;

        // Roll for 1-2 drops: guaranteed first roll, 40% chance for second
        int rollCount = rng.NextSingle() < 0.4f ? 2 : 1;

        for (int r = 0; r < rollCount; r++)
        {
            // Weighted random selection
            int roll = rng.Next(totalWeight);
            int cumulative = 0;

            foreach (var entry in table)
            {
                cumulative += entry.Weight;
                if (roll < cumulative)
                {
                    var item = ItemRegistry.GetItem(entry.ItemId);
                    if (item == null) break;

                    int quantity = entry.MinQuantity == entry.MaxQuantity
                        ? entry.MinQuantity
                        : rng.Next(entry.MinQuantity, entry.MaxQuantity + 1);

                    // Slight position offset for multiple drops
                    float offsetX = r == 0 ? 0 : (rng.NextSingle() - 0.5f) * 0.6f;
                    float offsetY = r == 0 ? 0 : (rng.NextSingle() - 0.5f) * 0.6f;

                    drops.Add(new GroundLootDrop
                    {
                        DropId = $"loot_{Interlocked.Increment(ref _dropCounter)}",
                        ItemId = entry.ItemId,
                        Quantity = quantity,
                        X = enemyX + offsetX,
                        Y = enemyY + offsetY,
                        EligiblePeerIds = eligiblePeerIds,
                        Rarity = item.Rarity,
                        CreatedAtServerTick = createdAtServerTick,
                        DespawnAfterTicks = DeterministicLootGenerator.DespawnAfterTicks,
                        DropMode = mode,
                    });

                    break;
                }
            }
        }

        return drops;
    }

    /// <summary>
    /// Generate a single drop with a deterministic seed (delegates to DeterministicLootGenerator).
    /// </summary>
    public static GroundLootDrop? GenerateDropWithSeed(
        string enemySubType,
        float x,
        float y,
        string seed,
        HashSet<string> eligiblePeerIds,
        LootDropMode mode,
        long createdAtServerTick)
        => DeterministicLootGenerator.GenerateDropWithSeed(
            enemySubType, x, y, seed, eligiblePeerIds, mode, createdAtServerTick);

    // =========================================================================
    // ELIGIBILITY DETERMINATION
    // =========================================================================

    /// <summary>
    /// Determine which peer IDs are eligible for loot from a killed enemy.
    /// Implements RuneScape-style tagging rules.
    /// 
    /// RULES:
    ///   - If enemy.TaggedBy is set → only that peer (solo kill)
    ///   - If tagger is in a party → all party members (round-robin handled at pickup)
    ///   - If no tag → anyone (shouldn't happen, but fallback)
    /// </summary>
    /// <param name="taggedBy">Peer ID of the first attacker (from Entity.TaggedBy).</param>
    /// <param name="partyMembers">
    /// Party member peer IDs of the tagger (empty if solo). Includes the tagger themselves.
    /// </param>
    /// <returns>Set of eligible peer IDs.</returns>
    public static HashSet<string> DetermineEligibility(string? taggedBy, IEnumerable<string>? partyMembers)
    {
        var eligible = new HashSet<string>();

        if (string.IsNullOrEmpty(taggedBy))
        {
            // No tag — open to anyone (shouldn't normally happen)
            return eligible; // Empty = anyone
        }

        // Solo or party
        if (partyMembers != null)
        {
            foreach (var member in partyMembers)
            {
                eligible.Add(member);
            }
        }

        // Always include the tagger
        eligible.Add(taggedBy);

        return eligible;
    }

    /// <summary>
    /// Check if a specific peer can pick up a loot drop (legacy — prefer LootDropVisibility).
    /// </summary>
    public static bool CanPickUp(GroundLootDrop drop, string peerId, int currentServerTick = int.MaxValue)
        => LootDropVisibility.CanPickup(drop, peerId, currentServerTick);
}

/// <summary>
/// Manages active ground loot drops in the overworld. Tracks despawn timers
/// and provides query methods for the frontend.
/// </summary>
public sealed class LootDropManager
{
    private readonly ConcurrentDictionary<string, GroundLootDrop> _drops = new();

    /// <summary>All active drops.</summary>
    public IEnumerable<GroundLootDrop> ActiveDrops => _drops.Values;

    /// <summary>Add a new ground loot drop.</summary>
    public void AddDrop(GroundLootDrop drop)
    {
        _drops[drop.DropId] = drop;
    }

    /// <summary>Upsert a drop from a P2P sync payload, rebasing ticks onto the local clock.</summary>
    public void SyncDrop(PeerLootDropEntry entry, int localTick, long senderTick)
    {
        long age = Math.Max(0, senderTick - entry.CreatedAtServerTick);
        long localCreatedAt = localTick - age;
        var eligible = new HashSet<string>(entry.EligiblePeerIds);
        var drop = new GroundLootDrop
        {
            DropId = entry.DropId,
            ItemId = entry.ItemId,
            Quantity = entry.Quantity,
            X = entry.X,
            Y = entry.Y,
            EligiblePeerIds = eligible,
            CreatedAtServerTick = localCreatedAt,
            DespawnAfterTicks = entry.DespawnAfterTicks,
            DropMode = ParseDropMode(entry.DropMode),
            GenerationSeed = entry.GenerationSeed,
            IsCollected = entry.IsCollected,
            CollectedByPeerId = entry.CollectedByPeerId,
            CollectedAtTick = entry.CollectedAtTick,
        };

        var item = ItemRegistry.GetItem(entry.ItemId);
        if (item != null)
            drop.Rarity = item.Rarity;

        _drops[entry.DropId] = drop;
    }

    /// <summary>Try to pick up a drop (marks collected and removes from ground).</summary>
    public GroundLootDrop? TryPickUp(string dropId, string peerId, int currentServerTick)
    {
        if (!_drops.TryGetValue(dropId, out var drop)) return null;
        if (!LootDropVisibility.CanPickup(drop, peerId, currentServerTick)) return null;

        drop.IsCollected = true;
        drop.CollectedByPeerId = peerId;
        drop.CollectedAtTick = currentServerTick;
        _drops.TryRemove(dropId, out _);
        return drop;
    }

    /// <summary>Apply a remote pickup event (autonomous removal).</summary>
    public bool ApplyRemotePickup(string dropId, string pickedUpByPeerId, long serverTick)
    {
        if (!_drops.TryGetValue(dropId, out var drop) || drop.IsCollected)
            return false;

        drop.IsCollected = true;
        drop.CollectedByPeerId = pickedUpByPeerId;
        drop.CollectedAtTick = serverTick;
        _drops.TryRemove(dropId, out _);
        return true;
    }

    /// <summary>Expand a drop to fair game (solo → anyone).</summary>
    public bool ApplyFairGame(string dropId)
    {
        if (!_drops.TryGetValue(dropId, out var drop))
            return false;

        drop.EligiblePeerIds.Clear();
        return true;
    }

    /// <summary>Get drops visible to a specific peer (filtered by eligibility).</summary>
    public List<GroundLootDrop> GetDropsForPeer(string peerId, int currentServerTick)
    {
        return _drops.Values
            .Where(d => LootDropVisibility.IsVisibleTo(d, peerId, currentServerTick))
            .ToList();
    }

    /// <summary>
    /// Process tick: expand solo eligibility and remove expired drops.
    /// Returns drop IDs that became fair game or expired (for broadcasting).
    /// </summary>
    public LootTickResult ProcessTick(int currentServerTick)
    {
        var fairGame = new List<string>();
        var expired = new List<string>();

        foreach (var (id, drop) in _drops)
        {
            if (LootDropVisibility.ExpandToFairGame(drop, currentServerTick))
                fairGame.Add(id);

            if (LootDropVisibility.IsExpired(drop, currentServerTick))
                expired.Add(id);
        }

        foreach (var id in expired)
            _drops.TryRemove(id, out _);

        return new LootTickResult(fairGame, expired);
    }

    public static PeerLootDropEntry ToSyncEntry(GroundLootDrop drop)
    {
        return new PeerLootDropEntry
        {
            DropId = drop.DropId,
            ItemId = drop.ItemId,
            Quantity = drop.Quantity,
            X = drop.X,
            Y = drop.Y,
            EligiblePeerIds = drop.EligiblePeerIds.ToArray(),
            IsCollected = drop.IsCollected,
            CreatedAtServerTick = drop.CreatedAtServerTick,
            DespawnAfterTicks = drop.DespawnAfterTicks,
            DropMode = drop.DropMode.ToString().ToLowerInvariant(),
            GenerationSeed = drop.GenerationSeed,
            CollectedByPeerId = drop.CollectedByPeerId,
            CollectedAtTick = drop.CollectedAtTick,
        };
    }

    private static LootDropMode ParseDropMode(string mode) => mode switch
    {
        "partyrotation" => LootDropMode.PartyRotation,
        "partyanyone" => LootDropMode.PartyAnyOne,
        "elitepersonal" => LootDropMode.ElitePersonal,
        _ => LootDropMode.Solo,
    };
}

public readonly record struct LootTickResult(IReadOnlyList<string> FairGameDropIds, IReadOnlyList<string> ExpiredDropIds);
