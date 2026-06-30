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
            var callsWithoutMethodNames = FindActorTickScheduleCallsWithoutMethodNames(text);
            Assert.Empty(callsWithoutMethodNames);
        }
    }

    [Fact]
    public void Actor_tick_docs_scan_detects_schedule_calls_without_method_names()
    {
        const string text = """
            context.ScheduleActorTick<MatchmakingActor>("default", TimeSpan.FromMilliseconds(250), TickBacklogPolicy.Coalesce);
            context.ScheduleActiveActorTicks<RoomActor>(
                TimeSpan.FromMilliseconds(50),
                TickBacklogPolicy.SkipIfPending);
            context.ScheduleActorTick<MatchmakingActor>(
                "default",
                TimeSpan.FromMilliseconds(250),
                TickBacklogPolicy.Coalesce,
                nameof(MatchmakingBehavior.TickAsync));
            """;

        var callsWithoutMethodNames = FindActorTickScheduleCallsWithoutMethodNames(text);

        Assert.Equal(2, callsWithoutMethodNames.Count);
    }

    private static List<string> FindActorTickScheduleCallsWithoutMethodNames(string text)
    {
        var callsWithoutMethodNames = new List<string>();
        var searchStart = 0;
        while (TryFindNextActorTickScheduleCall(text, searchStart, out var callStart))
        {
            var callEnd = FindScheduleCallEnd(text, callStart);
            if (callEnd < 0)
            {
                callsWithoutMethodNames.Add(text[callStart..]);
                break;
            }

            var callText = text.Substring(callStart, callEnd - callStart + 1);
            if (!callText.Contains("nameof(", StringComparison.Ordinal))
            {
                callsWithoutMethodNames.Add(callText);
            }

            searchStart = callEnd + 1;
        }

        return callsWithoutMethodNames;
    }

    private static bool TryFindNextActorTickScheduleCall(string text, int startIndex, out int callStart)
    {
        var nextFixedActorTick = text.IndexOf("ScheduleActorTick<", startIndex, StringComparison.Ordinal);
        var nextActiveActorTicks = text.IndexOf("ScheduleActiveActorTicks<", startIndex, StringComparison.Ordinal);

        if (nextFixedActorTick < 0)
        {
            callStart = nextActiveActorTicks;
            return nextActiveActorTicks >= 0;
        }

        if (nextActiveActorTicks < 0)
        {
            callStart = nextFixedActorTick;
            return true;
        }

        callStart = Math.Min(nextFixedActorTick, nextActiveActorTicks);
        return true;
    }

    private static int FindScheduleCallEnd(string text, int callStart)
    {
        var openParen = text.IndexOf('(', callStart);
        if (openParen < 0)
        {
            return -1;
        }

        var depth = 0;
        for (var i = openParen; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
                continue;
            }

            if (text[i] != ')')
            {
                continue;
            }

            depth--;
            if (depth != 0)
            {
                continue;
            }

            var semicolonIndex = i + 1;
            while (semicolonIndex < text.Length && char.IsWhiteSpace(text[semicolonIndex]))
            {
                semicolonIndex++;
            }

            return semicolonIndex < text.Length && text[semicolonIndex] == ';'
                ? semicolonIndex
                : -1;
        }

        return -1;
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
