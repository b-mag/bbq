using Carcosa.Server.Gameplay;
using Xunit;

namespace Carcosa.Tests;

public class QuestProgressionTests
{
    [Fact]
    public void PickupHusk_ThenMerek_BindsNecronomicon()
    {
        var q = new QuestProgression();
        var pick = q.PickupWorldObject("old_book_husk");
        Assert.True(pick.Success);
        Assert.True(q.HasKeyItem("old_book_husk"));

        var talk = q.TalkToNpc("npc_merek");
        Assert.True(talk.Advanced);
        Assert.True(q.HasKeyItem("necronomicon"));
        Assert.False(q.HasKeyItem("old_book_husk"));
        Assert.Equal(NecronomiconQuestStage.SentToDocks.ToString(), talk.Stage);
    }

    [Fact]
    public void DrownedDockVictory_UnlocksSeeBeyond()
    {
        var q = new QuestProgression();
        q.PickupWorldObject("old_book_husk");
        q.TalkToNpc("npc_merek");
        q.NotifyDungeonComplete("drowned_dock", true);
        Assert.Contains("see_beyond", q.Snapshot().NecronomiconFunctions);

        var use = q.UseKeyItem("necronomicon");
        Assert.True(use.Success);
        Assert.True(q.Snapshot().SeeBeyondActive);
        Assert.Equal("temple", q.Snapshot().SeeBeyondAreaId);
    }

    [Fact]
    public void Friends_TogglePersistsInSnapshot()
    {
        var q = new QuestProgression();
        Assert.True(q.ToggleFriend("peer-a", "Cassilda"));
        Assert.True(q.IsFriend("peer-a"));
        Assert.False(q.ToggleFriend("peer-a", "Cassilda"));
        Assert.False(q.IsFriend("peer-a"));
    }
}

public class DigSystemTests
{
    [Fact]
    public void Dig_WithoutShovel_Fails()
    {
        var q = new QuestProgression();
        var inv = new PlayerInventory();
        var result = DigSystem.TryDig(q, inv, 100, 100, 7);
        Assert.False(result.Success);
        Assert.Contains("Shovel", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dig_OnClueSpot_WithShovel_GrantsArtifact()
    {
        var q = new QuestProgression();
        q.GrantKeyItem(QuestProgression.ShovelItemId);
        var inv = new PlayerInventory();
        var art = DigSystem.Artifacts[0];
        var result = DigSystem.TryDig(q, inv, art.X, art.Y, 7);
        Assert.True(result.Success);
        Assert.Equal(art.Id, result.ItemId);
        Assert.True(q.HasKeyItem(art.Id));
    }
}
