using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class ActorActivationContextTests
{
    [Fact]
    public async Task Context_activation_is_available_at_start_survives_reload_and_changes_on_recreation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "LakonaActorActivation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            HotfixManager? manager = null;
            await using var root = new ServiceCollection()
                .AddLakonaGameServerActors()
                .AddSingleton<IHotfixRuntimeAccessor>(_ => manager!)
                .AddSingleton<HotfixActorLifecycleInvoker>()
                .AddSingleton<IActorLifecycleDispatcher, HotfixActorLifecycleDispatcher>()
                .BuildServiceProvider();
            await using var hotfix = new HotfixManager(
                new CurrentDirectoryHotfixAssemblySource(directory, "ActivationProbe.dll"),
                [typeof(ProbeActor).Assembly.GetName().Name!],
                rootServices: root);
            manager = hotfix;
            var catalog = root.GetRequiredService<ActorActivationCatalog>();
            var actorId = ActorId.From("activation/probe");

            WriteHotfix(directory, "v1");
            var firstLoad = await hotfix.ReloadAsync(cancellationToken);
            Assert.True(firstLoad.Succeeded, firstLoad.ErrorMessage);
            var firstSnapshot = ((IHotfixRuntimeAccessor)hotfix).Current;
            await catalog.CreateAsync<ProbeActor>(actorId, cancellationToken);
            var first = await ReadActorAsync();
            var activationId = first.Context.ActivationId;
            Assert.NotEqual(default, activationId);
            Assert.Equal(activationId, first.StartedActivationId);
            Assert.Equal("v1", first.StartedByVersion);
            Assert.Equal(1, first.StartCount);

            await catalog.EnsureAsync<ProbeActor>(actorId, cancellationToken);
            Assert.Same(first, await ReadActorAsync());
            Assert.Equal(activationId, first.Context.ActivationId);

            WriteHotfix(directory, "v2");
            var reload = await hotfix.ReloadAsync(cancellationToken);
            Assert.True(reload.Succeeded, reload.ErrorMessage);
            Assert.NotSame(firstSnapshot, ((IHotfixRuntimeAccessor)hotfix).Current);
            var afterReload = await ReadActorAsync();
            Assert.Same(first, afterReload);
            Assert.Equal(activationId, afterReload.Context.ActivationId);
            Assert.Equal(1, afterReload.StartCount);

            await catalog.DestroyAsync<ProbeActor>(actorId, cancellationToken);
            await catalog.CreateAsync<ProbeActor>(actorId, cancellationToken);
            var replacement = await ReadActorAsync();
            Assert.NotSame(first, replacement);
            Assert.Equal(first.Context.Id, replacement.Context.Id);
            Assert.NotEqual(default, replacement.Context.ActivationId);
            Assert.NotEqual(activationId, replacement.Context.ActivationId);
            Assert.Equal(replacement.Context.ActivationId, replacement.StartedActivationId);
            Assert.Equal("v2", replacement.StartedByVersion);
            Assert.Equal(1, replacement.StartCount);
            Assert.Equal(activationId, first.Context.ActivationId);
            await catalog.DestroyAsync<ProbeActor>(actorId, cancellationToken);

            ValueTask<ProbeActor> ReadActorAsync() => catalog.AskAsync<ProbeActor, ProbeActor>(
                actorId, static (actor, _) => new ValueTask<ProbeActor>(actor), cancellationToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteHotfix(string directory, string version)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Tests;
            using Microsoft.Extensions.DependencyInjection;

            [HotfixBehaviorOf(typeof(ActorActivationContextTests.ProbeActor))]
            public sealed class ProbeBehavior
            {
                [ActorStart]
                public ValueTask StartAsync(ActorActivationContextTests.ProbeActor self, ActorStartCall call)
                {
                    self.StartedActivationId = self.Context.ActivationId;
                    self.StartedByVersion = "{{version}}";
                    self.StartCount++;
                    return default;
                }
            }

            public sealed class Registration : IHotfixGeneratedServiceRegistration
            {
                public void Register(IServiceCollection services) => services.AddSingleton<ProbeBehavior>();
            }
            """;
        var references = HotfixTestMetadataReferences.CreateDefaultReferences(
            typeof(ProbeActor), typeof(HotfixManager), typeof(ServiceCollection));
        var compilation = CSharpCompilation.Create(
            "ActivationProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(Path.Combine(directory, "ActivationProbe.dll"));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    public sealed class ProbeActor : Actor<string>
    {
        public ActorActivationId StartedActivationId;
        public string? StartedByVersion;
        public int StartCount;
    }
}
