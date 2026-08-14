using System.Security.Cryptography;
using System.Text;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// How loot eligibility behaves over time for a drop.
/// </summary>
public enum LootDropMode
{
    Solo,
    PartyRotation,
    PartyAnyOne,
    ElitePersonal,
}

/// <summary>
/// Deterministic loot generation for distributed elite drops and seed verification.
/// </summary>
public static class DeterministicLootGenerator
{
    public const int TicksPerSecond = 20;
    public const int FairGameAfterTicks = 60 * TicksPerSecond;
    public const int DespawnAfterTicks = 120 * TicksPerSecond;

    /// <summary>
    /// Generate a single loot drop using a deterministic seed.
    /// </summary>
    public static GroundLootDrop? GenerateDropWithSeed(
        string enemySubType,
        float x,
        float y,
        string seed,
        HashSet<string> eligiblePeerIds,
        LootDropMode mode,
        long createdAtServerTick)
    {
        var table = LootSystem.GetLootTable(enemySubType);
        if (table == null || table.Length == 0)
            return null;

        var rng = CreateRngFromSeed(seed);
        int totalWeight = 0;
        foreach (var entry in table) totalWeight += entry.Weight;

        int roll = rng.Next(totalWeight);
        int cumulative = 0;

        foreach (var entry in table)
        {
            cumulative += entry.Weight;
            if (roll >= cumulative) continue;

            var item = ItemRegistry.GetItem(entry.ItemId);
            if (item == null) return null;

            int quantity = entry.MinQuantity == entry.MaxQuantity
                ? entry.MinQuantity
                : rng.Next(entry.MinQuantity, entry.MaxQuantity + 1);

            float offsetX = (rng.NextSingle() - 0.5f) * 0.3f;
            float offsetY = (rng.NextSingle() - 0.5f) * 0.3f;

            return new GroundLootDrop
            {
                DropId = $"loot_{ComputeDropIdSuffix(seed)}",
                ItemId = entry.ItemId,
                Quantity = quantity,
                X = x + offsetX,
                Y = y + offsetY,
                EligiblePeerIds = eligiblePeerIds,
                Rarity = item.Rarity,
                CreatedAtServerTick = createdAtServerTick,
                DespawnAfterTicks = DespawnAfterTicks,
                DropMode = mode,
                GenerationSeed = seed,
            };
        }

        return null;
    }

    /// <summary>
    /// Compute the deterministic seed for an elite personal drop.
    /// </summary>
    public static string ComputeEliteLootSeed(
        string eliteId,
        string peerId,
        long serverTick,
        string worldId)
    {
        var input = $"{eliteId}:{peerId}:{serverTick}:{worldId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verify that a drop was generated from the expected seed.
    /// </summary>
    public static bool VerifyDropFromSeed(GroundLootDrop drop, string expectedSeed)
    {
        if (string.IsNullOrEmpty(drop.GenerationSeed))
            return false;

        return string.Equals(drop.GenerationSeed, expectedSeed, StringComparison.OrdinalIgnoreCase);
    }

    private static Random CreateRngFromSeed(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Random(BitConverter.ToInt32(hash, 0));
    }

    private static string ComputeDropIdSuffix(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"drop:{seed}"));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
