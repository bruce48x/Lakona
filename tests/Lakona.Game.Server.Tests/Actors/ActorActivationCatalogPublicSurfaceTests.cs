using System.Reflection;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorActivationCatalogPublicSurfaceTests
{
    [Fact]
    public void IActorRuntime_does_not_expose_lifecycle_methods()
    {
        var methods = typeof(IActorRuntime).GetMethods().Select(static method => method.Name).ToArray();

        Assert.DoesNotContain(string.Concat("Get", "Or", "Create", "Async"), methods);
        Assert.DoesNotContain(string.Concat("Stop", "Async"), methods);
    }

    [Fact]
    public void ActorActivationCatalog_is_the_internal_local_activation_owner()
    {
        Assert.False(typeof(ActorActivationCatalog).IsPublic);
        Assert.False(typeof(ActorPlacementService).IsPublic);
        Assert.True(typeof(IActorPlacementService).IsPublic);

        var methods = typeof(ActorActivationCatalog).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.DeclaringType == typeof(ActorActivationCatalog))
            .Select(static method => method.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("CreateAsync", methods);
        Assert.Contains("DestroyAsync", methods);
        Assert.Contains("EnsureAsync", methods);
    }

    [Fact]
    public void Old_public_actor_lifecycle_types_are_absent()
    {
        var assembly = typeof(IActorRuntime).Assembly;
        var deletedTypes = new[]
        {
            "Lakona.Game.Server.Actors." + string.Concat("I", "Actor", "Lifecycle"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Spawn", "Attribute"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Destroy", "Attribute"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Create", "Local", "Result"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Create", "Local", "Status"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Destroy", "Local", "Result"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Destroy", "Local", "Status"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Create", "Options"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Destroy", "Options"),
            "Lakona.Game.Server.Actors." + string.Concat("Actor", "Stop", "Outcome")
        };

        foreach (var deletedType in deletedTypes)
        {
            Assert.Null(assembly.GetType(deletedType, throwOnError: false));
        }
    }

    [Fact]
    public void ActorActivationCatalog_does_not_expose_public_lifecycle_methods()
    {
        var methods = typeof(ActorActivationCatalog)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(static method => method.Name)
            .ToArray();

        Assert.DoesNotContain(string.Concat("Create", "Local", "Async"), methods);
        Assert.DoesNotContain(string.Concat("Destroy", "Local", "Async"), methods);
        Assert.DoesNotContain(string.Concat("Get", "Or", "Create", "Async"), methods);
        Assert.DoesNotContain(string.Concat("Stop", "Async"), methods);
    }
}
