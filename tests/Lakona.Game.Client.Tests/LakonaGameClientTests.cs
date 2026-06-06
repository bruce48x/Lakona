using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
using Lakona.Game.Client.Sessions;
using Xunit;

namespace Lakona.Game.Client.Tests;

public sealed class LakonaGameClientTests
{
    [Fact]
    public async Task MainEntryProcessesReliablePushAndAppliesAckOutcome()
    {
        var client = new LakonaGameClient();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var applied = new List<string>();
        client.StartSession(session);

        var result = await client.ProcessReliablePushAsync(
            ReliablePushSequence.From(1),
            "matched",
            (payload, _) =>
            {
                applied.Add(payload);
                return ValueTask.CompletedTask;
            },
            (_, _) => ValueTask.FromResult(ReliablePushAckOutcome.StateRefreshRequired()),
            TestContext.Current.CancellationToken);

        Assert.True(result.Decision.ShouldApply);
        Assert.Equal("matched", Assert.Single(applied));
        Assert.Equal(ClientSessionPhase.RefreshRequired, client.Snapshot.Phase);
        Assert.Equal(session, client.Snapshot.Session);
        Assert.Equal(1, client.Snapshot.LastReliableSequence);
    }

    [Fact]
    public async Task MainEntryMakesStateLostTerminalUntilNewSession()
    {
        var client = new LakonaGameClient();
        var session = new GameSessionKey("player-a", "session-a", 1);
        client.StartSession(session);

        await client.ProcessReliablePushAsync(
            ReliablePushSequence.From(1),
            "matched",
            (_, _) => ValueTask.CompletedTask,
            (_, _) => ValueTask.FromResult(ReliablePushAckOutcome.SessionMismatch()),
            TestContext.Current.CancellationToken);
        client.MarkReconnecting();

        Assert.Equal(ClientSessionPhase.StateLost, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.Session);

        var next = new GameSessionKey("player-a", "session-b", 2);
        client.StartSession(next);

        Assert.Equal(ClientSessionPhase.Active, client.Snapshot.Phase);
        Assert.Equal(next, client.Snapshot.Session);
    }

    [Fact]
    public void MainEntryAppliesSessionTerminationNotice()
    {
        var client = new LakonaGameClient();
        var session = new GameSessionKey("player-a", "session-a", 1);
        var notice = new SessionTerminationNotice(
            session,
            SessionTerminationReason.Policy,
            "Removed.");
        client.StartSession(session, lastReliableSequence: 7);

        client.ApplySessionTerminationNotice(notice);

        Assert.Equal(ClientSessionPhase.Terminated, client.Snapshot.Phase);
        Assert.Null(client.Snapshot.Session);
        Assert.Equal(0, client.Snapshot.LastReliableSequence);
        Assert.Same(notice, client.Snapshot.Termination);
    }
}
