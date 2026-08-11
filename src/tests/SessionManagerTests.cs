using Carcosa.Server.Game;
using Carcosa.Server.Network;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for SessionManager — lobby lifecycle and player management.
/// </summary>
public class SessionManagerTests
{
    private SessionManager CreateSessionManager()
    {
        var cm = new ConnectionManager();
        var gl = new GameLoop(cm);
        return new SessionManager(cm, gl);
    }

    [Fact]
    public void AddPlayer_FirstPlayerBecomesHost()
    {
        var sm = CreateSessionManager();
        var session = sm.AddPlayer("p1", "Alice");
        Assert.True(session.IsHost);
        Assert.Equal("p1", sm.HostId);
    }

    [Fact]
    public void AddPlayer_SubsequentPlayersAreNotHost()
    {
        var sm = CreateSessionManager();
        sm.AddPlayer("p1", "Alice");
        var session2 = sm.AddPlayer("p2", "Bob");
        Assert.False(session2.IsHost);
    }

    [Fact]
    public void RemovePlayer_ReassignsHost()
    {
        var sm = CreateSessionManager();
        sm.AddPlayer("p1", "Alice");
        sm.AddPlayer("p2", "Bob");

        sm.RemovePlayer("p1");
        Assert.Equal("p2", sm.HostId);
    }

    [Fact]
    public void SelectClass_SetsPlayerClass()
    {
        var sm = CreateSessionManager();
        sm.AddPlayer("p1", "Alice");
        sm.SelectClass("p1", "detective");

        var info = sm.GetSessionInfo();
        Assert.Equal("detective", info.Players[0].SelectedClass);
    }

    [Fact]
    public void SelectClass_RejectsInvalidClass()
    {
        var sm = CreateSessionManager();
        sm.AddPlayer("p1", "Alice");
        sm.SelectClass("p1", "wizard"); // Invalid

        var info = sm.GetSessionInfo();
        Assert.Null(info.Players[0].SelectedClass);
    }

    [Fact]
    public void State_StartsInLobby()
    {
        var sm = CreateSessionManager();
        Assert.Equal(SessionState.Lobby, sm.State);
    }
}
