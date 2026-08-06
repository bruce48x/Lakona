using Shared.Gameplay;
using Shared.Interfaces;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class FrameSyncSimulationTests
{
    [Fact]
    public void IdenticalStartAndFramesProduceIdenticalWorldStates()
    {
        var left = new FrameSyncSimulation(CreateStart());
        var right = new FrameSyncSimulation(CreateStart());

        for (var frameNumber = 1; frameNumber <= 200; frameNumber++)
        {
            var frame = CreateFrame(frameNumber);
            var leftStep = Assert.Single(left.SubmitFrame(frame).Steps);
            var rightStep = Assert.Single(right.SubmitFrame(CreateFrame(frameNumber)).Steps);
            AssertWorldsEqual(leftStep.WorldState, rightStep.WorldState);
            Assert.Equal(leftStep.MatchEnd?.WinnerPlayerId, rightStep.MatchEnd?.WinnerPlayerId);
            Assert.Equal(leftStep.MatchEnd?.Tick, rightStep.MatchEnd?.Tick);
        }
    }

    [Fact]
    public void OutOfOrderFramesWaitForGapAndThenAdvanceContinuously()
    {
        var simulation = new FrameSyncSimulation(CreateStart());

        Assert.Empty(simulation.SubmitFrame(CreateFrame(2)).Steps);
        var advance = simulation.SubmitFrame(CreateFrame(1));

        Assert.Equal(2, advance.Steps.Length);
        Assert.Equal(1, advance.Steps[0].WorldState.Tick);
        Assert.Equal(2, advance.Steps[1].WorldState.Tick);
        Assert.Equal(2, simulation.LastAppliedFrame);
    }

    [Fact]
    public void DuplicateFrameDoesNotRunBattleCalculationTwice()
    {
        var simulation = new FrameSyncSimulation(CreateStart());
        var first = simulation.SubmitFrame(CreateFrame(1));

        var duplicate = simulation.SubmitFrame(CreateFrame(1));

        Assert.Single(first.Steps);
        Assert.Empty(duplicate.Steps);
        Assert.Equal(1, simulation.LastAppliedFrame);
    }

    [Fact]
    public void ServerRoomCodeDoesNotOwnArenaSimulation()
    {
        var root = FindRepositoryRoot();
        var serverRoomRoots = new[]
        {
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "App", "Rooms"),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", "Hotfix", "Rooms")
        };
        var sources = serverRoomRoots
            .SelectMany(path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(sources, source => source.Contains("ArenaSimulation", StringComparison.Ordinal));
        Assert.DoesNotContain(sources, source => source.Contains("PublishWorldState", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Contains("PublishFrames(room, self.State.FrameHistory)", StringComparison.Ordinal));
    }

    private static FrameSyncStart CreateStart()
    {
        return new FrameSyncStart
        {
            ProtocolVersion = FrameSyncProtocol.Version,
            RoomId = "room-1",
            MatchId = "match-1",
            RandomSeed = 123456,
            FixedDeltaSeconds = FrameSyncProtocol.FixedDeltaSeconds,
            MaxPlayers = 4,
            Players =
            [
                new FrameSyncPlayer { PlayerId = "p2", SeatIndex = 1 },
                new FrameSyncPlayer { PlayerId = "p1", SeatIndex = 0 }
            ]
        };
    }

    private static FrameSyncFrame CreateFrame(int frame)
    {
        return new FrameSyncFrame
        {
            MatchId = "match-1",
            Frame = frame,
            Inputs =
            [
                new InputMessage
                {
                    PlayerId = "p2",
                    MoveX = frame % 2 == 0 ? -0.75f : 0.25f,
                    MoveY = 0.5f,
                    ServerTick = frame
                },
                new InputMessage
                {
                    PlayerId = "p1",
                    MoveX = 0.8f,
                    MoveY = frame % 3 == 0 ? -0.4f : 0.1f,
                    ServerTick = frame
                }
            ]
        };
    }

    private static void AssertWorldsEqual(WorldState expected, WorldState actual)
    {
        Assert.Equal(expected.Tick, actual.Tick);
        Assert.Equal(expected.RoundRemainingSeconds, actual.RoundRemainingSeconds);
        Assert.Equal(expected.ArenaHalfExtentX, actual.ArenaHalfExtentX);
        Assert.Equal(expected.ArenaHalfExtentY, actual.ArenaHalfExtentY);
        Assert.Equal(expected.Players.Count, actual.Players.Count);
        for (var index = 0; index < expected.Players.Count; index++)
        {
            var left = expected.Players[index];
            var right = actual.Players[index];
            Assert.Equal(left.PlayerId, right.PlayerId);
            Assert.Equal(left.X, right.X);
            Assert.Equal(left.Y, right.Y);
            Assert.Equal(left.Vx, right.Vx);
            Assert.Equal(left.Vy, right.Vy);
            Assert.Equal(left.State, right.State);
            Assert.Equal(left.Alive, right.Alive);
            Assert.Equal(left.Mass, right.Mass);
            Assert.Equal(left.Radius, right.Radius);
        }

        Assert.Equal(expected.Pickups.Count, actual.Pickups.Count);
        for (var index = 0; index < expected.Pickups.Count; index++)
        {
            Assert.Equal(expected.Pickups[index].Type, actual.Pickups[index].Type);
            Assert.Equal(expected.Pickups[index].X, actual.Pickups[index].X);
            Assert.Equal(expected.Pickups[index].Y, actual.Pickups[index].Y);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException();
    }
}
