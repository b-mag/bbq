using Carcosa.Server.P2P;
using Xunit;

namespace Carcosa.Tests;

public class WorldShardTests
{
    [Fact]
    public void SelectBestShard_WhenAllKnownShardsAreFull_UsesNextShard()
    {
        var shardPopulations = new Dictionary<string, int>
        {
            ["carcosa-00"] = 100,
            ["carcosa-01"] = 100,
        };

        var selected = WorldShard.SelectBestShard(shardPopulations);

        Assert.Equal("carcosa-02", selected);
    }

    [Fact]
    public void GetNextAvailableShardId_PrefersTheNextAvailableShard()
    {
        var shardPopulations = new Dictionary<string, int>
        {
            ["carcosa-00"] = 100,
            ["carcosa-01"] = 20,
            ["carcosa-02"] = 44,
        };

        var selected = WorldShard.GetNextAvailableShardId("carcosa-00", shardPopulations);

        Assert.Equal("carcosa-01", selected);
    }
}
