// =============================================================================
// ProgressionSystem.cs — Player Leveling & XP Awards
// =============================================================================
//
// OVERVIEW:
// Handles XP gain and level-up logic. Players earn XP from killing enemies
// and completing objectives. Leveling up grants permanent stat increases.
//
// XP CURVE:
//   Level N requires N * 100 XP to reach. XP resets to 0 on level-up.
//   Level 2: 200 XP, Level 10: 1000 XP, Level 50: 5000 XP.
//   This creates a linear progression that's easy to understand.
//
// STAT GAINS PER LEVEL:
//   - MaxStamina: +10 (THE most valuable — Dark Souls endurance philosophy)
//   - MaxHP: +5
//   - StaminaRegenRate: +0.5/sec (barely noticeable per level, significant over 50)
//
// XP SOURCES:
//   - Gronk kill: 25 XP (beginner enemy, quick levels early)
//   - Future: dungeon completion, elite kills, quests
//
// LEVEL CAP: 50 (soft cap for demo). No XP gained at cap.
// =============================================================================

using Carcosa.Server.Game;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Handles XP awards and level-up processing for player entities.
/// Static class — all state lives on the Entity.
/// </summary>
public static class ProgressionSystem
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>Maximum level (soft cap for demo).</summary>
    public const int MaxLevel = 50;

    /// <summary>XP awarded for killing a Gronk.</summary>
    public const int GronkKillXP = 25;

    /// <summary>Party bonus when 2+ eligible members share a kill (Diablo-style).</summary>
    public const float PartyXpBonus = 0.10f;

    /// <summary>Max stamina gained per level.</summary>
    private const float StaminaPerLevel = 10f;

    /// <summary>Max HP gained per level.</summary>
    private const int HPPerLevel = 5;

    /// <summary>Stamina regen rate gained per level (points/sec).</summary>
    private const float RegenPerLevel = 0.5f;

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>
    /// Calculate XP required to reach the next level from the current level.
    /// Formula: nextLevel * 100. (Level 1→2 needs 200 XP, Level 49→50 needs 5000 XP)
    /// </summary>
    public static int XPForNextLevel(int currentLevel)
    {
        if (currentLevel >= MaxLevel) return int.MaxValue; // Can't level past cap
        return (currentLevel + 1) * 100;
    }

    /// <summary>
    /// Award XP to a player entity. Handles level-up if XP threshold is reached.
    /// Returns true if the player leveled up (for UI notification).
    /// 
    /// Can level up multiple times from a single large XP award (unlikely in practice
    /// but handled correctly for robustness).
    /// </summary>
    /// <param name="player">The player entity to award XP to.</param>
    /// <param name="xp">Amount of XP to award.</param>
    /// <returns>True if one or more level-ups occurred.</returns>
    public static bool AwardXP(Entity player, int xp)
    {
        if (player.Level >= MaxLevel) return false;

        player.XP += xp;
        bool leveledUp = false;

        // Check for level-up (potentially multiple)
        while (player.Level < MaxLevel && player.XP >= XPForNextLevel(player.Level))
        {
            player.XP -= XPForNextLevel(player.Level);
            player.Level++;
            ApplyLevelUpStats(player);
            leveledUp = true;

            Console.WriteLine($"[Progression] Player leveled up to {player.Level}! " +
                $"MaxStamina={player.MaxStamina}, MaxHP={player.MaxHealth}");
        }

        // Clamp XP at cap
        if (player.Level >= MaxLevel)
        {
            player.XP = 0;
        }

        player.IsDirty = true;
        return leveledUp;
    }

    /// <summary>
    /// Apply stat gains for a single level-up.
    /// </summary>
    private static void ApplyLevelUpStats(Entity player)
    {
        player.MaxStamina += StaminaPerLevel;
        player.Stamina = player.MaxStamina; // Full stamina on level-up (reward!)
        player.MaxHealth += HPPerLevel;
        player.Health = player.MaxHealth;    // Full heal on level-up (reward!)
        player.StaminaRegenRate += RegenPerLevel;
    }

    /// <summary>
    /// Get XP reward for killing an enemy by SubType.
    /// </summary>
    public static int GetKillXP(string enemySubType)
    {
        return enemySubType switch
        {
            "gronk" => GronkKillXP,
            "cultist_torch" or "cultist_acolyte" or "cultist_dagger" => 15,
            "cultist_shotgun" or "cultist_lightning" or "cultist_chanter" => 20,
            "cult_leader" => 50,
            "boss_warehouse" => 200,
            _ when enemySubType.StartsWith("elite_", StringComparison.OrdinalIgnoreCase) => GronkKillXP * 5,
            _ => 10,
        };
    }

    /// <summary>Kill XP scaled to the dungeon's average party level.</summary>
    public static int GetScaledKillXp(string enemySubType, int dungeonLevel)
        => DungeonRules.ScaleXp(GetKillXP(enemySubType), dungeonLevel);

    /// <summary>
    /// Compute XP awarded to each eligible peer for a kill.
    /// Full base XP to each eligible member; +PartyXpBonus when 2+ eligible (Diablo-style).
    /// </summary>
    public static int ComputeSharedKillXp(string enemySubType, int eligibleCount)
    {
        var baseXp = GetKillXP(enemySubType);
        if (eligibleCount >= 2)
            return (int)Math.Round(baseXp * (1f + PartyXpBonus));
        return baseXp;
    }

    /// <summary>
    /// Calculate total stats for a given level (for display purposes).
    /// </summary>
    public static (float MaxStamina, int MaxHP, float RegenRate) GetStatsForLevel(int level)
    {
        float stamina = 100f + (level - 1) * StaminaPerLevel;
        int hp = 100 + (level - 1) * HPPerLevel;
        float regen = 40f + (level - 1) * RegenPerLevel;
        return (stamina, hp, regen);
    }

    /// <summary>
    /// Apply absolute level/XP and recompute derived max stats from level 1 baseline.
    /// Used when loading a save.
    /// </summary>
    public static void ApplyLoadedProgression(Entity player, int level, int xp)
    {
        level = Math.Clamp(level, 1, MaxLevel);
        player.Level = level;
        player.XP = xp;
        var (maxStamina, maxHp, regen) = GetStatsForLevel(level);
        player.MaxStamina = maxStamina;
        player.MaxHealth = maxHp;
        player.StaminaRegenRate = regen;
        player.Health = maxHp;
        player.Stamina = maxStamina;
        player.IsDirty = true;
    }
}
