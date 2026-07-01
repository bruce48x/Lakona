using System.Reflection;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorHostingPublicSurfaceTests
{
    [Fact]
    public void IActorRuntime_does_not_expose_lifecycle_methods()
    {
        var methods = typeof(IActorRuntime).GetMethods().Select(static method => method.Name).ToArray();

        Assert.DoesNotContain("GetOrCreateAsync", methods);
        Assert.DoesNotContain("StopAsync", methods);
    }

    [Fact]
    public void ActorHosting_is_the_public_lifecycle_entry_point()
    {
        var methods = typeof(ActorHosting).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.DeclaringType == typeof(ActorHosting))
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["CreateAsync", "DestroyAsync", "EnsureAsync"], methods);
    }

    [Fact]
    public void Old_public_actor_lifecycle_types_are_absent()
    {
        var assembly = typeof(IActorRuntime).Assembly;
        var deletedTypes = new[]
        {
            "Lakona.Game.Server.Actors.IActorLifecycle",
            "Lakona.Game.Server.Actors.ActorSpawnAttribute",
            "Lakona.Game.Server.Actors.ActorDestroyAttribute",
            "Lakona.Game.Server.Actors.ActorCreateLocalResult",
            "Lakona.Game.Server.Actors.ActorCreateLocalStatus",
            "Lakona.Game.Server.Actors.ActorDestroyLocalResult",
            "Lakona.Game.Server.Actors.ActorDestroyLocalStatus",
            "Lakona.Game.Server.Actors.ActorCreateOptions",
            "Lakona.Game.Server.Actors.ActorDestroyOptions",
            "Lakona.Game.Server.Actors.ActorStopOutcome"
        };

        foreach (var deletedType in deletedTypes)
        {
            Assert.Null(assembly.GetType(deletedType, throwOnError: false));
        }
    }

    [Fact]
    public void LakonaActorRuntime_does_not_expose_public_lifecycle_methods()
    {
        var methods = typeof(LakonaActorRuntime)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(static method => method.Name)
            .ToArray();

        Assert.DoesNotContain("CreateLocalAsync", methods);
        Assert.DoesNotContain("DestroyLocalAsync", methods);
        Assert.DoesNotContain("GetOrCreateAsync", methods);
        Assert.DoesNotContain("StopAsync", methods);
    }
}
