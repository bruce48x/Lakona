using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixActorBoundaryAnalyzerTests
{
    [Fact]
    public async Task Reports_actor_business_methods()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public sealed class UserActor : Actor
            {
                public Task<int> LoginAsync(string password)
                {
                    return Task.FromResult(1);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ULGHOTFIX011", diagnostic.Id);
    }

    [Fact]
    public async Task Allows_state_and_lifecycle_hooks()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            public sealed class RoomActor : Actor
            {
                internal readonly Dictionary<string, string> Members = new();

                protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
                {
                    return default;
                }

                protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
                {
                    return default;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_private_and_static_helpers_on_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;

            public sealed class MatchmakingActor : Actor
            {
                private static int NormalizeRoomSize(int size)
                {
                    return size <= 0 ? 4 : size;
                }

                private int GetScore()
                {
                    return 10;
                }
            }
            """);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("ULGHOTFIX011", diagnostic.Id));
    }

    [Fact]
    public async Task Reports_non_actor_hotfix_behavior_target()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Hotfix.Abstractions;

            public sealed class ArenaSimulation
            {
            }

            [HotfixBehaviorOf(typeof(ArenaSimulation))]
            public static partial class ArenaSimulationBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX017");
        Assert.Contains("ArenaSimulation", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_duplicate_hotfix_behavior_for_actor()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct UserId(string Value);
            public sealed class UserActor : Actor<UserId>
            {
            }

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
            }

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class PlayerSessionBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX018");
        Assert.Contains("UserActor", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_behavior_that_is_not_static_partial()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct RoomId(string Value);
            public sealed class RoomActor : Actor<RoomId>
            {
            }

            [HotfixBehaviorOf(typeof(RoomActor))]
            public static class RoomBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX019");
        Assert.Contains("RoomBehavior", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_behavior_name_that_does_not_match_actor_prefix()
    {
        var diagnostics = await AnalyzerTestHost.RunAsync("""
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            public readonly record struct MatchmakingQueueId(string Value);
            public sealed class MatchmakingActor : Actor<MatchmakingQueueId>
            {
            }

            [HotfixBehaviorOf(typeof(MatchmakingActor))]
            public static partial class MatchmakingQueueBehavior
            {
            }
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "ULGHOTFIX020");
        Assert.Contains("MatchmakingBehavior", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
