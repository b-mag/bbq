using Carcosa.Server.P2P;
using Xunit;

namespace Carcosa.Tests;

public class PeerExchangeTests
{
    [Fact]
    public void ParseArgs_WhenDisableCacheBootstrapFlagIsPresent_DisablesBootstrap()
    {
        var settings = PeerExchangeSettings.FromArgs(new[] { "--disable-cache-bootstrap" });

        Assert.False(settings.AllowCacheBootstrap);
    }

    [Fact]
    public void ParseArgs_WhenClearPeerCacheFlagIsPresent_FlagsForCacheClear()
    {
        var settings = PeerExchangeSettings.FromArgs(new[] { "--clear-peer-cache" });

        Assert.True(settings.ClearCacheOnStartup);
    }
}
