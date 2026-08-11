using Carcosa.Server.Cryptol;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for CryptolStore — currency persistence and balance management.
/// </summary>
public class CryptolStoreTests
{
    [Fact]
    public void GetBalance_ReturnsZeroForUnknownPlayer()
    {
        var store = new CryptolStore(Path.GetTempFileName());
        Assert.Equal(0, store.GetBalance("nonexistent"));
    }

    [Fact]
    public void AwardCryptol_IncreasesBalance()
    {
        var store = new CryptolStore(Path.GetTempFileName());
        var newBalance = store.AwardCryptol("player1", 100);
        Assert.Equal(100, newBalance);
        Assert.Equal(100, store.GetBalance("player1"));
    }

    [Fact]
    public void AwardCryptol_Accumulates()
    {
        var store = new CryptolStore(Path.GetTempFileName());
        store.AwardCryptol("p1", 50);
        store.AwardCryptol("p1", 75);
        Assert.Equal(125, store.GetBalance("p1"));
    }

    [Fact]
    public void AwardCryptolBatch_AwardsMultiplePlayers()
    {
        var store = new CryptolStore(Path.GetTempFileName());
        store.AwardCryptolBatch(new[] { "a", "b", "c" }, 1000);
        Assert.Equal(1000, store.GetBalance("a"));
        Assert.Equal(1000, store.GetBalance("b"));
        Assert.Equal(1000, store.GetBalance("c"));
    }
}
