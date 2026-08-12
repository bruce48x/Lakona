using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Actors;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Generators;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixUnloadTests
{
    [Fact]
    public void Dispatch_table_version_changes_after_replace()
    {
        var first = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>());
        var second = new HotfixDispatchTable(2, Array.Empty<HotfixMethodBinding>());

        HotfixDispatch.Replace(first);
        Assert.Equal(1, HotfixDispatch.Current.Version);

        HotfixDispatch.Replace(second);
        Assert.Equal(2, HotfixDispatch.Current.Version);
    }

    [Fact]
    public async Task Routed_CallAsync_does_not_retain_behavior_method_group()
    {
        var loadContextReference = await RunHotfixCallAndUnloadAsync(static async hotfix =>
        {
            var result = await hotfix.CallRouteAsync(hotfix.JoinAsync, 41);
            Assert.Equal(42, result);
        });

        await ForceFullCollectionAsync(loadContextReference);
    }

    [Fact]
    public async Task Local_PostAsync_does_not_retain_behavior_method_group()
    {
        var loadContextReference = await RunHotfixCallAndUnloadAsync(static async hotfix =>
        {
            await hotfix.PostLocalAsync(hotfix.RunTickAsync, 7);
            var lastTick = await hotfix.ReadLastTickAsync();
            Assert.Equal(7, lastTick);
        });

        await ForceFullCollectionAsync(loadContextReference);
    }

    [Fact]
    public async Task Route_CallAsync_value_task_waits_for_behavior_completion()
    {
        var loadContextReference = await RunHotfixCallAndUnloadAsync(static async hotfix =>
        {
            hotfix.ResetGate();
            var call = hotfix.CallRouteNoResultAsync(hotfix.RunTickAsync, -1);

            await hotfix.WaitForGateEnteredAsync();
            Assert.False(call.IsCompleted);

            hotfix.ReleaseGate();
            await call;
            Assert.Equal(-1, await hotfix.ReadLastTickAsync());
        });

        await ForceFullCollectionAsync(loadContextReference);
    }

    [Fact]
    public async Task Local_PostAsync_dead_actor_throws_actor_call_exception()
    {
        var loadContextReference = await RunHotfixCallAndUnloadAsync(
            static async hotfix =>
            {
                var exception = await Assert.ThrowsAsync<ActorCallException>(() =>
                    hotfix.PostLocalAsync(hotfix.RunTickAsync, 7));

                Assert.Equal(ActorCallStatus.ActorNotFound, exception.Status);
            },
            createActor: false);

        await ForceFullCollectionAsync(loadContextReference);
    }

    private static async Task<WeakReference> RunHotfixCallAndUnloadAsync(
        Func<HotfixUnloadHarness, Task> invokeAsync,
        bool createActor = true)
    {
        var assemblies = CompileGeneratedHotfixAssemblies();
        var loadContext = new SharedAssemblyCollectibleLoadContext();
        var loadContextReference = new WeakReference(loadContext);

        try
        {
            await ExecuteHotfixCallAsync(loadContext, assemblies, invokeAsync, createActor);
        }
        finally
        {
            loadContext.Unload();
        }

        return loadContextReference;
    }

    private static async Task ExecuteHotfixCallAsync(
        AssemblyLoadContext loadContext,
        GeneratedHotfixAssemblies assemblies,
        Func<HotfixUnloadHarness, Task> invokeAsync,
        bool createActor)
    {
        var appAssembly = loadContext.LoadFromStream(new MemoryStream(assemblies.AppBytes));
        var hotfixAssembly = loadContext.LoadFromStream(new MemoryStream(assemblies.HotfixBytes));
        var scan = HotfixBehaviorScanner.Scan(hotfixAssembly);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));

        var table = new HotfixDispatchTable(1, scan.Methods, scan.Services, scan.ActorMethods);
        var services = new ServiceCollection()
            .AddLakonaGameServerActors();
        foreach (var moduleType in table.ModuleTypes)
        {
            services.AddSingleton(moduleType);
        }

        services.AddSingleton<IHotfixRuntimeAccessor>(provider =>
            new FixedHotfixRuntimeAccessor(new HotfixRuntimeSnapshot(new HotfixServiceInvoker(), provider)));
        await using var provider = services.BuildServiceProvider();

        table.ValidateModuleActivation(provider);
        HotfixDispatch.Replace(table);

        HotfixUnloadHarness? harness = null;
        var actorCreated = false;
        try
        {
            harness = CreateHarness(appAssembly, hotfixAssembly, provider);
            if (createActor)
            {
                await CreateActorAsync(
                    provider.GetRequiredService<ActorHosting>(),
                    harness.ActorType,
                    ActorId.From(harness.RoomId),
                    TestContext.Current.CancellationToken);
                actorCreated = true;
            }

            await invokeAsync(harness);
        }
        finally
        {
            if (actorCreated && harness is not null)
            {
                await DestroyActorAsync(
                    provider.GetRequiredService<ActorHosting>(),
                    harness.ActorType,
                    ActorId.From(harness.RoomId),
                    TestContext.Current.CancellationToken);
            }

            HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        }
    }

    private static HotfixUnloadHarness CreateHarness(
        Assembly appAssembly,
        Assembly hotfixAssembly,
        ServiceProvider provider)
    {
        var exportsType = hotfixAssembly.GetType("HotfixUnload.HotfixUnloadExports", throwOnError: true)!;
        var actorType = appAssembly.GetType("HotfixUnload.RoomActor", throwOnError: true)!;
        var actors = exportsType
            .GetMethod("CreateActorAccess", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(
                null,
                [
                    provider.GetRequiredService<IActorRuntime>(),
                    provider,
                    provider.GetRequiredService<RemoteActorOptions>(),
                    ThrowingActorPlacementService.Instance
                ])!;

        return new HotfixUnloadHarness(
            actors,
            actorType,
            GetStaticValue<string>(exportsType, "RoomId"),
            GetStaticValue<object>(exportsType, "JoinAsync"),
            GetStaticValue<object>(exportsType, "RunTickAsync"),
            hotfixAssembly.GetType(
                "Lakona.Game.Server.Hotfix.GeneratedHotfixActorSelectorExtensions",
                throwOnError: true)!,
            hotfixAssembly.GetType("HotfixUnload.RoomBehaviorGate", throwOnError: true)!,
            provider.GetRequiredService<IActorRuntime>());
    }

    private static GeneratedHotfixAssemblies CompileGeneratedHotfixAssemblies()
    {
        const string appSource = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("HotfixUnload.Hotfix")]

            namespace HotfixUnload;

            public sealed class RoomActor : Actor<string>
            {
                public int LastTick { get; set; }

                private static TaskCompletionSource CreateCompletionSource()
                {
                    return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            """;

        const string hotfixSource = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Abstractions.Actors;

            namespace HotfixUnload;

            [HotfixBehaviorOf(typeof(RoomActor))]
            public sealed partial class RoomBehavior
            {
                public ValueTask<int> JoinAsync(
                    RoomActor self,
                    int request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    self.LastTick = request;
                    return new ValueTask<int>(request + 1);
                }

                public ValueTask RunTickAsync(
                    RoomActor self,
                    int request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (request == -1)
                    {
                        RoomBehaviorGate.GateEntered.TrySetResult();
                        return WaitForGateAsync(self, cancellationToken);
                    }

                    self.LastTick = request;
                    return default;
                }

                private static async ValueTask WaitForGateAsync(RoomActor self, CancellationToken cancellationToken)
                {
                    await RoomBehaviorGate.ReleaseGate.Task.WaitAsync(cancellationToken);
                    self.LastTick = -1;
                }
            }

            internal static class RoomBehaviorGate
            {
                internal static TaskCompletionSource GateEntered { get; private set; } = CreateCompletionSource();

                internal static TaskCompletionSource ReleaseGate { get; private set; } = CreateCompletionSource();

                internal static void ResetGate()
                {
                    GateEntered = CreateCompletionSource();
                    ReleaseGate = CreateCompletionSource();
                }

                internal static void CompleteGate()
                {
                    ReleaseGate.TrySetResult();
                }

                private static TaskCompletionSource CreateCompletionSource()
                {
                    return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            public static class HotfixUnloadExports
            {
                public static string RoomId => "room-1";

                public static Func<RoomBehavior, HotfixActorCall<RoomActor, int, int>> JoinAsync =>
                    static behavior => behavior.JoinAsync;

                public static Func<RoomBehavior, HotfixActorPost<RoomActor, int>> RunTickAsync =>
                    static behavior => behavior.RunTickAsync;

                public static ActorAccess CreateActorAccess(
                    IActorRuntime runtime,
                    IServiceProvider services,
                    RemoteActorOptions options,
                    IActorPlacementService placement)
                {
                    return new ActorAccess(runtime, services, options, placement);
                }
            }
            """;

        var references = HotfixTestMetadataReferences.CreateDefaultReferences(
            typeof(Actor<>),
            typeof(HotfixBehaviorOfAttribute),
            typeof(HotfixDispatch),
            typeof(ValueTask),
            typeof(CancellationToken),
            typeof(IServiceProvider),
            typeof(NodeId),
            typeof(ServiceCollection));

        var appCompilation = RunHotfixGenerator(
            CSharpCompilation.Create(
                "HotfixUnload.App",
                [CSharpSyntaxTree.ParseText(appSource)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        var appBytes = EmitCompilation(appCompilation);

        var hotfixCompilation = RunHotfixGenerator(
            CSharpCompilation.Create(
                "HotfixUnload.Hotfix",
                [CSharpSyntaxTree.ParseText(hotfixSource)],
                references.Concat([MetadataReference.CreateFromImage(appBytes)]),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

        return new GeneratedHotfixAssemblies(appBytes, EmitCompilation(hotfixCompilation));
    }

    private static Compilation RunHotfixGenerator(CSharpCompilation compilation)
    {
        CSharpGeneratorDriver
            .Create(new HotfixGenerator())
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var generatorDiagnostics);

        var errors = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
        return outputCompilation;
    }

    private static byte[] EmitCompilation(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        }

        return stream.ToArray();
    }

    private static async Task CreateActorAsync(
        ActorHosting hosting,
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var method = typeof(ActorHosting)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == nameof(ActorHosting.CreateAsync) && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(actorType);

        var task = (ValueTask)method.Invoke(hosting, [actorId, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    private static async Task DestroyActorAsync(
        ActorHosting hosting,
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var method = typeof(ActorHosting)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(candidate => candidate.Name == nameof(ActorHosting.DestroyAsync) && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(actorType);

        var task = (ValueTask)method.Invoke(hosting, [actorId, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    private static T GetStaticValue<T>(Type type, string propertyName)
    {
        return (T)type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    private static async Task ForceFullCollectionAsync(WeakReference loadContextReference)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!loadContextReference.IsAlive)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.False(loadContextReference.IsAlive, "Hotfix AssemblyLoadContext should be collectible after actor method-group calls complete and the dispatch table is cleared.");
    }

    private sealed class SharedAssemblyCollectibleLoadContext : AssemblyLoadContext
    {
        public SharedAssemblyCollectibleLoadContext()
            : base(isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()));
        }
    }

    private sealed record GeneratedHotfixAssemblies(byte[] AppBytes, byte[] HotfixBytes);

    private sealed class FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot current) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = current;
    }

    private sealed class ThrowingActorPlacementService : IActorPlacementService
    {
        public static readonly ThrowingActorPlacementService Instance = new();

        private ThrowingActorPlacementService()
        {
        }

        public ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
            TKey key,
            ActorPlacementCreateMode createMode,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            throw new NotSupportedException("Placement is not part of this unload test.");
        }
    }

    private sealed class HotfixUnloadHarness(
        object actors,
        Type actorType,
        string roomId,
        object joinAsync,
        object runTickAsync,
        Type selectorExtensionsType,
        Type gateType,
        IActorRuntime runtime)
    {
        public Type ActorType { get; } = actorType;

        public string RoomId { get; } = roomId;

        public object JoinAsync { get; } = joinAsync;

        public object RunTickAsync { get; } = runTickAsync;

        private Type GateType { get; } = gateType;

        public async Task<int> CallRouteAsync(object method, int request)
        {
            var route = CreateSelector("Route");
            var callAsync = selectorExtensionsType
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(candidate =>
                    candidate.Name == "CallAsync" &&
                    candidate.ReturnType == typeof(ValueTask<int>) &&
                    candidate.GetParameters()[0].ParameterType == route.GetType());

            var call = (ValueTask<int>)callAsync.Invoke(null, [route, method, request, CancellationToken.None])!;
            return await call.ConfigureAwait(false);
        }

        public Task CallRouteNoResultAsync(object method, int request)
        {
            var route = CreateSelector("Route");
            var callAsync = selectorExtensionsType
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(candidate =>
                    candidate.Name == "CallAsync" &&
                    candidate.ReturnType == typeof(ValueTask) &&
                    candidate.GetParameters()[0].ParameterType == route.GetType());

            return ((ValueTask)callAsync.Invoke(null, [route, method, request, CancellationToken.None])!).AsTask();
        }

        public async Task PostLocalAsync(object method, int request)
        {
            var local = CreateSelector("Local");
            var postAsync = selectorExtensionsType
                .GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(candidate =>
                    candidate.Name == "PostAsync" &&
                    candidate.ReturnType == typeof(ValueTask) &&
                    candidate.GetParameters()[0].ParameterType == local.GetType());

            ValueTask call;
            try
            {
                call = (ValueTask)postAsync.Invoke(null, [local, method, request, CancellationToken.None])!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is ActorCallException actorCallException)
            {
                throw actorCallException;
            }

            await call.ConfigureAwait(false);
        }

        private object CreateSelector(string methodName)
        {
            var selector = actors.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(candidate =>
                    candidate.Name == methodName &&
                    candidate.IsGenericMethodDefinition &&
                    candidate.GetParameters().Length == 1 &&
                    candidate.GetParameters()[0].ParameterType == typeof(string))
                .MakeGenericMethod(ActorType);
            return selector.Invoke(actors, [RoomId])!;
        }

        public void ResetGate()
        {
            GateType.GetMethod("ResetGate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, []);
        }

        public async Task WaitForGateEnteredAsync()
        {
            var gate = (TaskCompletionSource)GateType
                .GetProperty("GateEntered", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

            await gate.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }

        public void ReleaseGate()
        {
            GateType.GetMethod("CompleteGate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, []);
        }

        public async Task<int> ReadLastTickAsync()
        {
            var result = await runtime.AskAsync(
                ActorType,
                ActorId.From(RoomId),
                static (actor, cancellationToken) => new ValueTask<object?>(
                    actor.GetType().GetProperty("LastTick", BindingFlags.Instance | BindingFlags.Public)!.GetValue(actor)),
                CancellationToken.None).ConfigureAwait(false);

            return Assert.IsType<int>(result);
        }
    }
}
