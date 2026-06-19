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
}
