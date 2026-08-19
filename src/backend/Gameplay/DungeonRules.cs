// =============================================================================
// DungeonRules.cs — Shared dungeon spawn, aggro, attack, and scaling rules
// =============================================================================
//
// Dungeons scale to average party level (AvgLevel). That scaled level also
// drives beginner-friendly combat:
//   - Enemies never spawn in the entrance foyer.
//   - Level 10 and below: no auto-aggro (attack to pull).
//   - Level 7 and below: melee only (no enemy projectiles).
//   - HP, damage, XP, and loot rarity all scale with AvgLevel.
// =============================================================================

using Carcosa.Server.Game;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Authoritative dungeon combat rules keyed off the instance's average party level.
/// </summary>
public static class DungeonRules
{
    /// <summary>Tiles around the player entrance where enemies must not spawn.</summary>
    public const float EntranceSafeRadius = 10f;

    /// <summary>At this dungeon level and below, enemies idle until attacked.</summary>
    public const int PassiveUntilAttackedMaxLevel = 10;

    /// <summary>At this dungeon level and below, enemies use melee only.</summary>
    public const int MeleeOnlyMaxLevel = 7;

    /// <summary>HP/damage/XP grow this much per level above 1.</summary>
    public const float ScalePerLevel = 0.12f;

    /// <summary>Dungeon / player level cap used for layout and scaling.</summary>
    public const int MaxLevel = 100;

    /// <summary>Default encounter: fixed enemies, no respawn. Waves/continuous are later styles.</summary>
    public const DungeonSpawnStyle DefaultSpawnStyle = DungeonSpawnStyle.Fixed;

    public static int ClampLevel(int dungeonLevel)
        => Math.Clamp(dungeonLevel, 1, MaxLevel);

    public static (int Width, int Height) MapSize(MapScenario scenario, int dungeonLevel)
    {
        var grow = (ClampLevel(dungeonLevel) - 1) / 10;
        return scenario switch
        {
            MapScenario.MountainCave => (60 + grow * 4, 50 + grow * 3),
            MapScenario.PallidSanctum => (100 + grow * 4, 100 + grow * 4),
            MapScenario.DrownedDock => (80 + grow * 6, 60 + grow * 4),
            _ => (80 + grow * 4, 60 + grow * 3),
        };
    }

    public static int TrashCount(int dungeonLevel)
        => Math.Clamp(6 + ClampLevel(dungeonLevel), 6, 48);

    public static int EliteCount(int dungeonLevel)
    {
        var level = ClampLevel(dungeonLevel);
        if (level < 8) return 0;
        return Math.Min(6, 1 + (level - 8) / 12);
    }

    public static SpawnPoint? PickFarthestEnemySpawn(TileMap map)
    {
        var (ex, ey) = GetEntrancePosition(map);
        SpawnPoint? farthest = null;
        var farthestDistSq = -1f;
        foreach (var sp in map.SpawnPoints)
        {
            if (sp.Type == SpawnPointType.Player) continue;
            if (!map.IsWalkable(sp.X, sp.Y)) continue;
            var dx = sp.X + 0.5f - ex;
            var dy = sp.Y + 0.5f - ey;
            var distSq = dx * dx + dy * dy;
            if (distSq > farthestDistSq)
            {
                farthestDistSq = distSq;
                farthest = sp;
            }
        }
        return farthest;
    }

    public static bool AutoAggro(int dungeonLevel)
        => Math.Max(1, dungeonLevel) > PassiveUntilAttackedMaxLevel;

    public static bool AllowsEnemyProjectiles(int dungeonLevel)
        => Math.Max(1, dungeonLevel) > MeleeOnlyMaxLevel;

    public static float ScaleFactor(int dungeonLevel)
    {
        var level = Math.Max(1, dungeonLevel);
        return 1f + (level - 1) * ScalePerLevel;
    }

    public static int ScaleStat(int baseValue, int dungeonLevel)
        => Math.Max(1, (int)Math.Round(baseValue * ScaleFactor(dungeonLevel)));

    public static int ScaleXp(int baseXp, int dungeonLevel)
        => ScaleStat(baseXp, dungeonLevel);

    /// <summary>Highest loot rarity a dungeon of this level can roll.</summary>
    public static ItemRarity MaxLootRarity(int dungeonLevel)
    {
        var level = Math.Max(1, dungeonLevel);
        if (level <= MeleeOnlyMaxLevel) return ItemRarity.Common;
        if (level <= PassiveUntilAttackedMaxLevel) return ItemRarity.Uncommon;
        if (level <= 20) return ItemRarity.Rare;
        return ItemRarity.Epic;
    }

    /// <summary>Player entrance tile, or the southern walkable fallback.</summary>
    public static (float X, float Y) GetEntrancePosition(TileMap map)
    {
        foreach (var sp in map.SpawnPoints)
        {
            if (sp.Type == SpawnPointType.Player && map.IsWalkable(sp.X, sp.Y))
                return (sp.X + 0.5f, sp.Y + 0.5f);
        }

        return map.FindPlayerSpawn(new Random(map.Seed));
    }

    public static bool IsNearEntrance(float x, float y, TileMap map)
    {
        var (ex, ey) = GetEntrancePosition(map);
        var dx = x - ex;
        var dy = y - ey;
        return dx * dx + dy * dy < EntranceSafeRadius * EntranceSafeRadius;
    }

    /// <summary>
    /// Pick an enemy spawn away from the entrance. Falls back to the farthest
    /// walkable spawn so tiny maps still place enemies somewhere.
    /// </summary>
    public static SpawnPoint? PickEnemySpawn(TileMap map, Random rng)
    {
        var (ex, ey) = GetEntrancePosition(map);
        var safe = new List<SpawnPoint>();
        SpawnPoint? farthest = null;
        var farthestDistSq = -1f;

        foreach (var sp in map.SpawnPoints)
        {
            if (sp.Type == SpawnPointType.Player) continue;
            if (!map.IsWalkable(sp.X, sp.Y)) continue;

            var dx = sp.X + 0.5f - ex;
            var dy = sp.Y + 0.5f - ey;
            var distSq = dx * dx + dy * dy;
            if (distSq > farthestDistSq)
            {
                farthestDistSq = distSq;
                farthest = sp;
            }

            if (distSq >= EntranceSafeRadius * EntranceSafeRadius)
                safe.Add(sp);
        }

        if (safe.Count > 0)
            return safe[rng.Next(safe.Count)];

        return farthest;
    }

    public static bool IsRangedEnemySubtype(string? subType)
    {
        var kind = subType ?? "";
        if (kind.StartsWith("elite_", StringComparison.OrdinalIgnoreCase))
            kind = kind["elite_".Length..];
        return kind is "cultist_chanter" or "cultist_dagger" or "cultist_shotgun" or "cultist_lightning";
    }

    public static bool UsesProjectiles(string? subType, int dungeonLevel)
        => AllowsEnemyProjectiles(dungeonLevel) && IsRangedEnemySubtype(subType);

    /// <summary>Generate a dungeon map sized for this party level.</summary>
    public static TileMap GenerateScaledMap(MapScenario scenario, int seed, int dungeonLevel)
    {
        var (w, h) = MapSize(scenario, dungeonLevel);
        return scenario switch
        {
            MapScenario.MountainCave => MapGenerator.GenerateCave(w, h, seed),
            MapScenario.PallidSanctum => MapGenerator.GenerateTemple(w, h, seed),
            MapScenario.DrownedDock => MapGenerator.GenerateDrownedDock(w, h, seed),
            _ => MapGenerator.Generate(w, h, seed),
        };
    }

    public static string NormalizeCursor(string? value)
        => value is "off" or "crosshair" or "sword" or "hand" ? value : "crosshair";
}

/// <summary>
/// How enemies populate a dungeon. Default is Fixed (style C). Waves is reserved
/// for a later coliseum / capture arena.
/// </summary>
public enum DungeonSpawnStyle
{
    Waves,
    Continuous,
    Fixed,
}
