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
    /// <summary>Ticks remaining before this drop despawns (600 ticks = 30 seconds).</summary>
    public int DespawnTicksRemaining { get; set; } = 600;
    /// <summary>Rarity of the dropped item (for rendering glow color on client).</summary>
    public ItemRarity Rarity { get; set; }
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
        string enemySubType, float enemyX, float enemyY, HashSet<string> eligiblePeerIds)
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
                    });

                    break;
                }
            }
        }

        return drops;
    }

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
    /// Check if a specific peer can pick up a loot drop.
    /// </summary>
    public static bool CanPickUp(GroundLootDrop drop, string peerId)
    {
        // Empty eligibility set = anyone can pick up
        if (drop.EligiblePeerIds.Count == 0) return true;
        return drop.EligiblePeerIds.Contains(peerId);
    }
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

    /// <summary>Try to pick up a drop (removes it from the ground).</summary>
    public GroundLootDrop? TryPickUp(string dropId, string peerId)
    {
        if (!_drops.TryGetValue(dropId, out var drop)) return null;
        if (!LootSystem.CanPickUp(drop, peerId)) return null;
        _drops.TryRemove(dropId, out _);
        return drop;
    }

    /// <summary>Get drops visible to a specific peer (filtered by eligibility).</summary>
    public List<GroundLootDrop> GetDropsForPeer(string peerId)
    {
        return _drops.Values
            .Where(d => LootSystem.CanPickUp(d, peerId))
            .ToList();
    }

    /// <summary>
    /// Process tick: decrement despawn timers and remove expired drops.
    /// Called every tick (20Hz) by the combat sync loop.
    /// </summary>
    public void ProcessTick()
    {
        var expired = new List<string>();
        foreach (var (id, drop) in _drops)
        {
            drop.DespawnTicksRemaining--;
            if (drop.DespawnTicksRemaining <= 0)
            {
                expired.Add(id);
            }
        }
        foreach (var id in expired)
        {
            _drops.TryRemove(id, out _);
        }
    }
}
