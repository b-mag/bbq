namespace Carcosa.Server.Gameplay;

/// <summary>
/// Single source of truth for loot drop visibility and pickup eligibility.
/// </summary>
public static class LootDropVisibility
{
    /// <summary>
    /// Master method: can this peer pick up this drop?
    /// </summary>
    public static bool CanPickup(GroundLootDrop drop, string peerId, int currentServerTick)
    {
        if (drop.IsCollected || IsExpired(drop, currentServerTick))
            return false;

        if (drop.EligiblePeerIds.Count == 0)
            return true;

        return drop.EligiblePeerIds.Contains(peerId);
    }

    /// <summary>
    /// Check if drop should be shown to this peer (visibility = can pickup).
    /// </summary>
    public static bool IsVisibleTo(GroundLootDrop drop, string peerId, int currentServerTick)
        => CanPickup(drop, peerId, currentServerTick);

    /// <summary>
    /// Expand solo-owned drops to fair game after 60 seconds.
    /// </summary>
    public static bool ExpandToFairGame(GroundLootDrop drop, int currentServerTick)
    {
        if (drop.IsCollected || drop.DropMode != LootDropMode.Solo)
            return false;

        if (drop.EligiblePeerIds.Count == 0)
            return false;

        long age = currentServerTick - drop.CreatedAtServerTick;
        if (age < DeterministicLootGenerator.FairGameAfterTicks)
            return false;

        drop.EligiblePeerIds.Clear();
        return true;
    }

    /// <summary>
    /// Check if a drop has expired and should despawn.
    /// </summary>
    public static bool IsExpired(GroundLootDrop drop, int currentServerTick)
        => !drop.IsCollected
           && (currentServerTick - drop.CreatedAtServerTick) > drop.DespawnAfterTicks;
}
