using Carcosa.Server.Gameplay;
using Carcosa.Server.P2P;
using Xunit;

namespace Carcosa.Tests;

public class LootDropVisibilityTests
{
    private static GroundLootDrop CreateDrop(
        HashSet<string>? eligible = null,
        LootDropMode mode = LootDropMode.Solo,
        long createdAt = 0)
    {
        return new GroundLootDrop
        {
            DropId = "loot_test",
            ItemId = "raw_gronk_meat",
            EligiblePeerIds = eligible ?? new HashSet<string> { "peer_a" },
            CreatedAtServerTick = createdAt,
            DespawnAfterTicks = DeterministicLootGenerator.DespawnAfterTicks,
            DropMode = mode,
        };
    }

    [Fact]
    public void CanPickup_SoloOwned_OnlyEligiblePeer()
    {
        var drop = CreateDrop(new HashSet<string> { "peer_a" });

        Assert.True(LootDropVisibility.CanPickup(drop, "peer_a", 100));
        Assert.False(LootDropVisibility.CanPickup(drop, "peer_b", 100));
    }

    [Fact]
    public void CanPickup_FairGame_AnyPeer()
    {
        var drop = CreateDrop(new HashSet<string>());

        Assert.True(LootDropVisibility.CanPickup(drop, "peer_a", 100));
        Assert.True(LootDropVisibility.CanPickup(drop, "peer_b", 100));
    }

    [Fact]
    public void ExpandToFairGame_SoloOnly_After60Seconds()
    {
        var drop = CreateDrop(new HashSet<string> { "peer_a" }, LootDropMode.Solo, createdAt: 0);

        Assert.False(LootDropVisibility.ExpandToFairGame(drop, DeterministicLootGenerator.FairGameAfterTicks - 1));
        Assert.True(LootDropVisibility.ExpandToFairGame(drop, DeterministicLootGenerator.FairGameAfterTicks));
        Assert.True(LootDropVisibility.CanPickup(drop, "peer_b", DeterministicLootGenerator.FairGameAfterTicks));
    }

    [Fact]
    public void ExpandToFairGame_PartyMode_NoChange()
    {
        var drop = CreateDrop(new HashSet<string> { "peer_a", "peer_b" }, LootDropMode.PartyAnyOne, createdAt: 0);

        Assert.False(LootDropVisibility.ExpandToFairGame(drop, DeterministicLootGenerator.FairGameAfterTicks + 100));
        Assert.False(LootDropVisibility.CanPickup(drop, "peer_c", DeterministicLootGenerator.FairGameAfterTicks + 100));
    }

    [Fact]
    public void IsExpired_After120Seconds()
    {
        var drop = CreateDrop(new HashSet<string> { "peer_a" }, createdAt: 0);

        Assert.False(LootDropVisibility.IsExpired(drop, DeterministicLootGenerator.DespawnAfterTicks));
        Assert.True(LootDropVisibility.IsExpired(drop, DeterministicLootGenerator.DespawnAfterTicks + 1));
    }

    [Fact]
    public void IsVisibleTo_HidesCollectedAndExpired()
    {
        var drop = CreateDrop(new HashSet<string> { "peer_a" });
        drop.IsCollected = true;

        Assert.False(LootDropVisibility.IsVisibleTo(drop, "peer_a", 100));

        drop.IsCollected = false;
        Assert.False(LootDropVisibility.IsVisibleTo(drop, "peer_a", DeterministicLootGenerator.DespawnAfterTicks + 1));
    }
}

public class DeterministicLootGeneratorTests
{
    [Fact]
    public void ComputeEliteLootSeed_IsDeterministic()
    {
        var seed1 = DeterministicLootGenerator.ComputeEliteLootSeed("elite_1", "peer_a", 500, "world_1");
        var seed2 = DeterministicLootGenerator.ComputeEliteLootSeed("elite_1", "peer_a", 500, "world_1");
        var seed3 = DeterministicLootGenerator.ComputeEliteLootSeed("elite_1", "peer_b", 500, "world_1");

        Assert.Equal(seed1, seed2);
        Assert.NotEqual(seed1, seed3);
    }

    [Fact]
    public void GenerateDropWithSeed_IsDeterministic()
    {
        var eligible = new HashSet<string> { "peer_a" };
        const string seed = "test-seed-value";

        var drop1 = DeterministicLootGenerator.GenerateDropWithSeed(
            "gronk", 10f, 20f, seed, eligible, LootDropMode.ElitePersonal, 100);
        var drop2 = DeterministicLootGenerator.GenerateDropWithSeed(
            "gronk", 10f, 20f, seed, eligible, LootDropMode.ElitePersonal, 100);

        Assert.NotNull(drop1);
        Assert.NotNull(drop2);
        Assert.Equal(drop1.ItemId, drop2.ItemId);
        Assert.Equal(drop1.Quantity, drop2.Quantity);
        Assert.Equal(drop1.DropId, drop2.DropId);
    }

    [Fact]
    public void GenerateDropWithSeed_EliteSubTypeFallsBackToBaseTable()
    {
        var drop = DeterministicLootGenerator.GenerateDropWithSeed(
            "elite_gronk", 0f, 0f, "elite-seed", new HashSet<string> { "peer_a" },
            LootDropMode.ElitePersonal, 10);

        Assert.NotNull(drop);
        Assert.False(string.IsNullOrEmpty(drop.ItemId));
    }

    [Fact]
    public void VerifyDropFromSeed_MatchesGenerationSeed()
    {
        var eligible = new HashSet<string> { "peer_a" };
        const string seed = "verify-seed";

        var drop = DeterministicLootGenerator.GenerateDropWithSeed(
            "gronk", 1f, 2f, seed, eligible, LootDropMode.ElitePersonal, 50);

        Assert.NotNull(drop);
        Assert.True(DeterministicLootGenerator.VerifyDropFromSeed(drop, seed));
        Assert.False(DeterministicLootGenerator.VerifyDropFromSeed(drop, "other-seed"));
    }
}

public class PeerMetricsTests
{
    [Fact]
    public void MetricsCalculator_FromMetrics_MapsFields()
    {
        var metrics = new PeerMetrics
        {
            PeerId = "peer_a",
            LatencyMs = 42,
            PacketLossRate = 0.01f,
            CpuUsagePercent = 30,
            CurrentUploadUtilization = 0.5f,
            CurrentDownloadUtilization = 0.2f,
            Uptime = TimeSpan.FromMinutes(5),
        };

        var score = MetricsCalculator.FromMetrics(metrics);

        Assert.Equal(42, score.LatencyMs);
        Assert.Equal(0.01f, score.PacketLoss);
        Assert.Equal(30, score.CpuUsage);
        Assert.Equal(0.5f, score.BandwidthUtilization);
    }

    [Fact]
    public void TaskAssignmentManager_AssignsLowestPeerId()
    {
        var identity = new PeerIdentity { PeerId = "peer_c", DisplayName = "C" };
        var mesh = new PeerMesh(identity);
        var manager = new TaskAssignmentManager(mesh, identity);

        var assignment = manager.AssignTask(
            "task_1",
            TaskTypes.EnemyAi,
            new[] { "peer_c", "peer_a", "peer_b" },
            10);

        Assert.Equal("peer_a", assignment.AssignedPeerId);
    }
}

public class LootDropManagerTests
{
    [Fact]
    public void TryPickUp_RespectsVisibilityRules()
    {
        var manager = new LootDropManager();
        var drop = new GroundLootDrop
        {
            DropId = "loot_1",
            ItemId = "raw_gronk_meat",
            EligiblePeerIds = new HashSet<string> { "peer_a" },
            CreatedAtServerTick = 0,
            DropMode = LootDropMode.Solo,
        };
        manager.AddDrop(drop);

        Assert.Null(manager.TryPickUp("loot_1", "peer_b", 100));
        Assert.NotNull(manager.TryPickUp("loot_1", "peer_a", 100));
    }

    [Fact]
    public void SyncDrop_RebasesTicksOntoLocalClock()
    {
        var manager = new LootDropManager();
        var entry = new PeerLootDropEntry
        {
            DropId = "loot_1",
            ItemId = "raw_gronk_meat",
            EligiblePeerIds = new[] { "peer_a" },
            CreatedAtServerTick = 5000,
            DropMode = "solo",
        };

        manager.SyncDrop(entry, localTick: 100, senderTick: 5120);

        var visible = manager.GetDropsForPeer("peer_a", 100);
        Assert.Single(visible);
        Assert.Equal(-20, visible[0].CreatedAtServerTick);

        var tickResult = manager.ProcessTick(DeterministicLootGenerator.FairGameAfterTicks - 20);
        Assert.Contains("loot_1", tickResult.FairGameDropIds);
    }

    [Fact]
    public void ApplyRemotePickup_RemovesDrop()
    {
        var manager = new LootDropManager();
        manager.AddDrop(new GroundLootDrop
        {
            DropId = "loot_1",
            ItemId = "raw_gronk_meat",
            EligiblePeerIds = new HashSet<string> { "peer_a" },
            CreatedAtServerTick = 0,
        });

        Assert.True(manager.ApplyRemotePickup("loot_1", "peer_a", 50));
        Assert.Empty(manager.GetDropsForPeer("peer_a", 50));
    }
}
