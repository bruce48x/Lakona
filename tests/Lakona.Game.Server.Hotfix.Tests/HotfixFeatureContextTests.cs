using Lakona.Game.Server.Hotfix.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureContextTests
{
    [Fact]
    public void Feature_context_public_methods_do_not_expose_legacy_actor_tick_apis()
    {
        var forbidden = new[]
        {
            string.Concat("Schedule", "ActorTick"),
            string.Concat("Schedule", "ActiveActorTicks")
        };
        var publicMethods = typeof(HotfixFeatureContext)
            .GetMethods()
            .Where(static method => method.DeclaringType == typeof(HotfixFeatureContext))
            .Select(static method => method.Name)
            .ToArray();

        foreach (var method in forbidden)
        {
            Assert.DoesNotContain(method, publicMethods);
        }
    }

    [Fact]
    public void Public_sources_do_not_expose_legacy_actor_tick_or_actor_context_timer_apis()
    {
        var repositoryRoot = FindRepositoryRoot();
        var forbidden = new[]
        {
            string.Concat("Schedule", "ActorTick"),
            string.Concat("Schedule", "ActiveActorTicks"),
            string.Concat("Hotfix", "ActorTick"),
            string.Concat("Register", "Timer")
        };
        var roots = new[] { "src", "tests", "samples" };
        var matches = roots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return forbidden
                    .Where(token => text.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(repositoryRoot, path)} contains {token}");
            })
            .ToArray();

        Assert.Empty(matches);
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
}
