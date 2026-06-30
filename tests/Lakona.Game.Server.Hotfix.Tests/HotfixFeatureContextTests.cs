using Lakona.Game.Server.Hotfix.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureContextTests
{
    [Fact]
    public void ScheduleActorTick_requires_explicit_method_name_parameter()
    {
        var method = typeof(HotfixFeatureContext).GetMethod(nameof(HotfixFeatureContext.ScheduleActorTick))!;

        var methodName = Assert.Single(method.GetParameters(), parameter => parameter.Name == "methodName");
        Assert.False(methodName.HasDefaultValue);
    }

    [Fact]
    public void ScheduleActiveActorTicks_requires_explicit_method_name_parameter()
    {
        var method = typeof(HotfixFeatureContext).GetMethod(nameof(HotfixFeatureContext.ScheduleActiveActorTicks))!;

        var methodName = Assert.Single(method.GetParameters(), parameter => parameter.Name == "methodName");
        Assert.False(methodName.HasDefaultValue);
    }

    [Fact]
    public void ScheduleActorTick_records_explicit_method_name()
    {
        var context = new HotfixFeatureContext();

        context.ScheduleActorTick<ContextTickActor>(
            "default",
            TimeSpan.FromMilliseconds(250),
            TickBacklogPolicy.Coalesce,
            nameof(ContextTickBehavior.TickAsync));

        var tick = Assert.Single(context.ActorTicks);
        Assert.Equal(HotfixActorTickMode.FixedActor, tick.Mode);
        Assert.Equal(typeof(ContextTickActor), tick.ActorType);
        Assert.Equal("default", tick.ActorId);
        Assert.Equal(nameof(ContextTickBehavior.TickAsync), tick.MethodName);
        Assert.Equal(TimeSpan.FromMilliseconds(250), tick.Interval);
        Assert.Equal(TickBacklogPolicy.Coalesce, tick.BacklogPolicy);
    }

    [Fact]
    public void ScheduleActiveActorTicks_records_explicit_method_name()
    {
        var context = new HotfixFeatureContext();

        context.ScheduleActiveActorTicks<ContextTickActor>(
            TimeSpan.FromMilliseconds(50),
            TickBacklogPolicy.SkipIfPending,
            nameof(ContextTickBehavior.TickAsync));

        var tick = Assert.Single(context.ActorTicks);
        Assert.Equal(HotfixActorTickMode.ActiveActors, tick.Mode);
        Assert.Equal(typeof(ContextTickActor), tick.ActorType);
        Assert.Equal("", tick.ActorId);
        Assert.Equal(nameof(ContextTickBehavior.TickAsync), tick.MethodName);
        Assert.Equal(TimeSpan.FromMilliseconds(50), tick.Interval);
        Assert.Equal(TickBacklogPolicy.SkipIfPending, tick.BacklogPolicy);
    }

    [Fact]
    public void ScheduleActorTick_rejects_blank_method_name()
    {
        var context = new HotfixFeatureContext();

        Assert.Throws<ArgumentException>(() =>
            context.ScheduleActorTick<ContextTickActor>(
                "default",
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce,
                ""));
    }

    [Fact]
    public void ScheduleActiveActorTicks_rejects_blank_method_name()
    {
        var context = new HotfixFeatureContext();

        Assert.Throws<ArgumentException>(() =>
            context.ScheduleActiveActorTicks<ContextTickActor>(
                TimeSpan.FromMilliseconds(50),
                TickBacklogPolicy.SkipIfPending,
                ""));
    }

    [Fact]
    public void Durable_docs_do_not_show_actor_tick_schedule_calls_without_method_names()
    {
        var repositoryRoot = FindRepositoryRoot();
        var docs = new[]
        {
            Path.Combine(repositoryRoot, "docs", "actor.md"),
            Path.Combine(repositoryRoot, "docs", "configuration.md"),
            Path.Combine(repositoryRoot, "docs", "hotfix", "architecture.md"),
            Path.Combine(repositoryRoot, "docs", "hotfix", "actor-behavior.md")
        };

        foreach (var path in docs)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain(
                "TickBacklogPolicy.Coalesce);",
                text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "TickBacklogPolicy.SkipIfPending);",
                text,
                StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Game.Server.Hotfix.Abstractions"))
                && Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class ContextTickActor
    {
    }

    private static class ContextTickBehavior
    {
        public static ValueTask TickAsync(ContextTickActor actor, HotfixActorTick tick)
        {
            _ = actor;
            _ = tick;
            return default;
        }
    }
}
