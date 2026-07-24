using System.ComponentModel;
using System.Reflection;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ActorApiBoundaryTests
{
    [Fact]
    public void IActorRuntime_is_hidden_as_generated_support_and_advanced_local_api()
    {
        var attribute = typeof(IActorRuntime).GetCustomAttribute<EditorBrowsableAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(EditorBrowsableState.Never, attribute.State);
    }

    [Fact]
    public void Game_server_readme_teaches_generated_selectors_before_raw_runtime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Game.Server", "README.md"));

        Assert.Contains("var actors = provider.GetRequiredService<ActorAccess>();", readme, StringComparison.Ordinal);
        Assert.Contains("var routed = await actors.Route<RoomActor>(roomId).CallAsync(", readme, StringComparison.Ordinal);
        Assert.Contains("var localOnly = await actors.Local<RoomActor>(roomId).CallAsync(", readme, StringComparison.Ordinal);
        Assert.Contains("static behavior => behavior.JoinAsync,", readme, StringComparison.Ordinal);
        Assert.Contains("Advanced Local Actor Runtime", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("var runtime = provider.GetRequiredService<IActorRuntime>();", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Actor_design_doc_classifies_raw_runtime_as_generated_support_escape_hatch()
    {
        var repositoryRoot = FindRepositoryRoot();
        var actorDoc = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "actor.md"));

        Assert.Contains("`IActorRuntime` is a generated-support and advanced local runtime API", actorDoc, StringComparison.Ordinal);
        Assert.Contains("not the recommended daily business API", actorDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void Actor_runtime_has_one_actor_model_and_no_kernel_conversion_layer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var serverSource = Path.Combine(repositoryRoot, "src", "Lakona.Game.Server");
        var sourceFiles = Directory.GetFiles(serverSource, "*.cs", SearchOption.AllDirectories);
        var source = string.Join(
            Environment.NewLine,
            sourceFiles.Select(File.ReadAllText));

        Assert.DoesNotContain("Internal.ActorKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActorRuntimeEnvelope", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KernelMessageInterceptorAdapter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcurrentDictionary<K.ActorId", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lakona.Game.Server"))
                && Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
