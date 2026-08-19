using Carcosa.Server.Game;
using Carcosa.Server.Gameplay;
using Xunit;

namespace Carcosa.Tests;

public class DungeonRulesTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void AutoAggro_OffAtLevel10AndBelow(int level, bool expected)
        => Assert.Equal(expected, DungeonRules.AutoAggro(level));

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void EnemyProjectiles_OffAtLevel7AndBelow(int level, bool expected)
        => Assert.Equal(expected, DungeonRules.AllowsEnemyProjectiles(level));

    [Fact]
    public void ScaleStat_GrowsWithPartyLevel()
    {
        Assert.Equal(15, DungeonRules.ScaleStat(15, 1));
        Assert.True(DungeonRules.ScaleStat(15, 10) > DungeonRules.ScaleStat(15, 1));
        Assert.True(DungeonRules.ScaleStat(15, 20) > DungeonRules.ScaleStat(15, 10));
    }

    [Fact]
    public void MaxLootRarity_UnlocksWithLevel()
    {
        Assert.Equal(ItemRarity.Common, DungeonRules.MaxLootRarity(5));
        Assert.Equal(ItemRarity.Uncommon, DungeonRules.MaxLootRarity(10));
        Assert.Equal(ItemRarity.Rare, DungeonRules.MaxLootRarity(15));
    }

    [Fact]
    public void PickEnemySpawn_DrownedDock_NeverNearEntrance()
    {
        for (int seed = 1; seed <= 8; seed++)
        {
            var map = MapGenerator.GenerateDrownedDock(80, 60, seed * 111);
            var (ex, ey) = DungeonRules.GetEntrancePosition(map);
            for (int i = 0; i < 20; i++)
            {
                var spawn = DungeonRules.PickEnemySpawn(map, new Random(seed * 100 + i));
                Assert.NotNull(spawn);
                Assert.NotEqual(SpawnPointType.Player, spawn!.Type);
                var dx = spawn.X + 0.5f - ex;
                var dy = spawn.Y + 0.5f - ey;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                Assert.True(dist >= DungeonRules.EntranceSafeRadius - 0.01f,
                    $"seed {seed} spawn ({spawn.X},{spawn.Y}) dist {dist:F1} from entrance ({ex:F1},{ey:F1})");
            }
        }
    }

    [Fact]
    public void ScaledKillXp_GrowsWithDungeonLevel()
    {
        var low = ProgressionSystem.GetScaledKillXp("cultist_torch", 1);
        var high = ProgressionSystem.GetScaledKillXp("cultist_torch", 20);
        Assert.True(high > low);
        Assert.True(ProgressionSystem.GetScaledKillXp("boss_warehouse", 1) > low);
    }

    [Fact]
    public void GenerateDungeonDrops_LowLevel_StaysCommon()
    {
        for (int i = 0; i < 30; i++)
        {
            var drops = LootSystem.GenerateDungeonDrops(
                "cultist_torch", 10, 10, new HashSet<string> { "p1" }, dungeonLevel: 3);
            foreach (var drop in drops)
                Assert.True(drop.Rarity <= ItemRarity.Common, drop.ItemId);
        }
    }

    [Fact]
    public void MapSize_GrowsWithDungeonLevel()
    {
        var small = DungeonRules.MapSize(MapScenario.DrownedDock, 1);
        var mid = DungeonRules.MapSize(MapScenario.DrownedDock, 50);
        var cap = DungeonRules.MapSize(MapScenario.DrownedDock, 100);
        Assert.True(mid.Width > small.Width);
        Assert.True(cap.Width >= mid.Width);
        Assert.Equal(DungeonRules.MapSize(MapScenario.DrownedDock, 100),
            DungeonRules.MapSize(MapScenario.DrownedDock, 999));
    }

    [Fact]
    public void TrashAndElites_ScaleWithLevel()
    {
        Assert.Equal(0, DungeonRules.EliteCount(7));
        Assert.True(DungeonRules.EliteCount(20) >= 1);
        Assert.True(DungeonRules.TrashCount(30) > DungeonRules.TrashCount(1));
        Assert.True(DungeonRules.TrashCount(100) <= 48);
    }

    [Fact]
    public void GenerateScaledMap_Temple_HasRoomsAndSouthEntrance()
    {
        var map = DungeonRules.GenerateScaledMap(MapScenario.PallidSanctum, 42, 12);
        Assert.True(map.Rooms.Length >= 3);
        Assert.Contains(map.SpawnPoints, s => s.Type == SpawnPointType.Player);
        Assert.True(map.Width >= 100);
    }
}

public class DungeonAiTests
{
    [Fact]
    public void LowLevelDungeon_DoesNotAutoAggroUntilAttacked()
    {
        var (state, ai, player, enemy) = MakeEncounter(avgLevel: 5);
        ai.Update(state);
        ai.Update(state);
        Assert.True(string.IsNullOrEmpty(enemy.AggroTargetId));
        Assert.Equal(0, enemy.VelocityX);

        ai.NotifyAttacked(enemy, player.Id);
        ai.Update(state);
        Assert.Equal(player.Id, enemy.AggroTargetId);
        Assert.True(Math.Abs(enemy.VelocityX) + Math.Abs(enemy.VelocityY) > 0
                    || Dist(player, enemy) <= 1.5f);
    }

    [Fact]
    public void HighLevelDungeon_AutoAggroOnSight()
    {
        var (state, ai, player, enemy) = MakeEncounter(avgLevel: 12);
        ai.Update(state);
        ai.Update(state);
        Assert.True(Math.Abs(enemy.VelocityX) + Math.Abs(enemy.VelocityY) > 0
                    || Dist(player, enemy) <= 1.6f);
    }

    [Fact]
    public void MeleeOnlyDungeon_DaggerCultist_DoesNotFireProjectiles()
    {
        var (state, ai, player, enemy) = MakeEncounter(avgLevel: 4);
        enemy.SubType = "cultist_dagger";
        enemy.X = player.X + 1.0f;
        enemy.Y = player.Y;
        ai.NotifyAttacked(enemy, player.Id);
        for (int i = 0; i < 25; i++)
            ai.Update(state);

        Assert.Empty(state.GetProjectiles().ToList());
    }

    [Fact]
    public void WaveSpawn_SkipsEntranceFoyer()
    {
        var map = MapGenerator.GenerateDrownedDock(80, 60, 42);
        var state = new GameState
        {
            Map = map,
            Phase = GamePhase.Playing,
            AvgLevel = 3,
        };
        var (px, py) = DungeonRules.GetEntrancePosition(map);
        var player = new Entity
        {
            Id = "player_1", Type = EntityType.Player, SubType = "b",
            X = px, Y = py, IsAlive = true, Health = 100, MaxHealth = 100, Level = 3
        };
        state.AddEntity(player);

        var ai = new AISystem();
        var waves = new WaveSystem(ai);
        waves.StartWaves(state);

        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Enemy) continue;
            var dx = entity.X - px;
            var dy = entity.Y - py;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            Assert.True(dist >= DungeonRules.EntranceSafeRadius - 1.5f,
                $"enemy at ({entity.X:F1},{entity.Y:F1}) dist {dist:F1}");
            Assert.False(DungeonRules.IsRangedEnemySubtype(entity.SubType),
                $"level 3 dungeon spawned ranged {entity.SubType}");
        }
    }

    [Fact]
    public void FixedPack_NoRespawnAfterClear()
    {
        var map = MapGenerator.GenerateDrownedDock(80, 60, 7);
        var state = new GameState { Map = map, Phase = GamePhase.Playing, AvgLevel = 8 };
        var ai = new AISystem();
        var waves = new WaveSystem(ai);
        waves.StartWaves(state);

        var packed = state.Entities.Values.Count(e => e.Type == EntityType.Enemy);
        Assert.True(packed >= 6);
        Assert.Equal(DungeonSpawnStyle.Fixed, waves.Style);

        foreach (var entity in state.Entities.Values)
        {
            if (entity.Type == EntityType.Enemy)
                entity.IsAlive = false;
        }

        waves.Update(state);
        Assert.True(waves.AllWavesComplete);
        Assert.Equal(0, waves.EnemiesRemaining);

        waves.Update(state);
        Assert.Equal(packed, state.Entities.Values.Count(e => e.Type == EntityType.Enemy));
        Assert.Equal(0, state.Entities.Values.Count(e => e.Type == EntityType.Enemy && e.IsAlive));
    }

    private static (GameState State, AISystem Ai, Entity Player, Entity Enemy) MakeEncounter(int avgLevel)
    {
        var map = MapGenerator.GenerateDrownedDock(80, 60, 11);
        var state = new GameState { Map = map, Phase = GamePhase.Playing, AvgLevel = avgLevel };
        var (px, py) = DungeonRules.GetEntrancePosition(map);
        var player = new Entity
        {
            Id = "player_1", Type = EntityType.Player, SubType = "b",
            X = px, Y = py, IsAlive = true, Health = 100, MaxHealth = 100, Level = avgLevel
        };
        var enemy = new Entity
        {
            Id = "enemy_1", Type = EntityType.Enemy, SubType = "cultist_torch",
            X = px,
            Y = py - 2.5f,
            IsAlive = true, Health = 20, MaxHealth = 20
        };
        state.AddEntity(player);
        state.AddEntity(enemy);
        var ai = new AISystem();
        ai.RegisterEnemy(enemy);
        return (state, ai, player, enemy);
    }

    private static float Dist(Entity a, Entity b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
