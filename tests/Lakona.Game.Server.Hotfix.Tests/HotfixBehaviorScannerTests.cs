extern alias GameServer;

using System.Reflection;
using System.Reflection.Emit;
using GameServer::Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Rpc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixBehaviorScannerTests
{
    [Fact]
    public void Actor_api_metadata_uses_canonical_key_names()
    {
        Assert.Equal("lakona-game.actor-api.version", HotfixActorApiMetadata.VersionKey);
        Assert.Equal("lakona-game.actor-api.actor-type", HotfixActorApiMetadata.ActorTypeKey);
        Assert.Equal("lakona-game.actor-api.method", HotfixActorApiMetadata.MethodKey);
        Assert.Equal("lakona-game.actor-api.request-type", HotfixActorApiMetadata.RequestTypeKey);
        Assert.Equal("lakona-game.actor-api.result-type", HotfixActorApiMetadata.ResultTypeKey);
        Assert.Equal("lakona-game.actor-api.method-key", HotfixActorApiMetadata.MethodKeyKey);
        Assert.Equal("void", HotfixActorApiMetadata.VoidResultType);
    }

    [Fact]
    public void Scanner_reads_hotfix_startup_actor_registrations()
    {
        var result = HotfixBehaviorScanner.Scan(
            typeof(StartupScanFixture.HotfixStartup).Assembly,
            [typeof(StartupScanFixture.HotfixStartup)]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var startup = Assert.Single(result.ActorStartups);
        Assert.Equal("matchmaking", startup.Name);
        var placement = Assert.Single(result.ActorPlacements);
        Assert.Equal(typeof(StartupScanFixture.RoomActor), placement.ActorType);
        Assert.Equal(typeof(ActorId), placement.KeyType);
    }

    [Fact]
    public void Scanner_reads_hotfix_startup_service_registrations()
    {
        var result = HotfixBehaviorScanner.Scan(
            typeof(StartupScanFixture.HotfixStartup).Assembly,
            [typeof(StartupScanFixture.HotfixStartup)]);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var service = Assert.Single(result.StartupServices);
        Assert.Equal(typeof(StartupScanFixture.IMarkerService), service.ServiceType);
        Assert.Equal(typeof(StartupScanFixture.MarkerService), service.ImplementationType);
    }

    [Fact]
    public void Scanner_rejects_non_static_hotfix_startup()
    {
        var result = HotfixBehaviorScanner.Scan(
            typeof(NonStaticStartupFixture.HotfixStartup).Assembly,
            [typeof(NonStaticStartupFixture.HotfixStartup)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("HotfixStartup", StringComparison.Ordinal) &&
            diagnostic.Contains("static class", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_invalid_hotfix_startup_configure_actors_signature()
    {
        var result = HotfixBehaviorScanner.Scan(
            typeof(InvalidStartupSignatureFixture.HotfixStartup).Assembly,
            [typeof(InvalidStartupSignatureFixture.HotfixStartup)]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Contains("ConfigureActors", StringComparison.Ordinal) &&
            diagnostic.Contains(nameof(ActorHostBuilder), StringComparison.Ordinal));
    }

    [Fact]
    public void Metadata_references_ignore_assembly_locations_that_no_longer_exist()
    {
        var missingAssemblyPath = Path.Combine(
            Path.GetTempPath(),
            "LakonaHotfixDeletedReferenceTests",
            Guid.NewGuid().ToString("N"),
            "DeletedReference.dll");

        var references = HotfixTestMetadataReferences.CreateDefaultReferences(
            [missingAssemblyPath],
            [typeof(object)]);

        Assert.DoesNotContain(references, reference =>
            string.Equals(reference.Display, missingAssemblyPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, reference =>
            string.Equals(reference.Display, typeof(object).Assembly.Location, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_builds_behavior_actor_api_descriptors()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingRequest(string Text);

            public sealed record PingReply(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
                public static ValueTask<PingReply> PingAsync(
                    this UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<PingReply>(new PingReply(request.Text));
                }
            }
            """);

        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var stableAssemblyName = fixture.StableAssembly.GetName().Name;
        var method = Assert.Single(scan.ActorMethods);
        Assert.Equal("StableGame.UserActor", method.ActorType.FullName);
        Assert.Equal("PingAsync", method.MethodName);
        Assert.Equal("StableGame.PingRequest", method.RequestType.FullName);
        Assert.Equal("StableGame.PingReply", method.ResultType.FullName);
        Assert.Contains($"actor:StableGame.UserActor, {stableAssemblyName}", method.MethodKey, StringComparison.Ordinal);
        Assert.Contains("|method:PingAsync|", method.MethodKey, StringComparison.Ordinal);
        Assert.Contains($"|request:StableGame.PingRequest, {stableAssemblyName}", method.MethodKey, StringComparison.Ordinal);
        Assert.Contains($"|result:StableGame.PingReply, {stableAssemblyName}", method.MethodKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_rejects_hotfix_local_actor_request_dto()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingReply(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            public sealed record PingRequest(string Text);

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
                public static ValueTask<PingReply> PingAsync(
                    this UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<PingReply>(new PingReply(request.Text));
                }
            }
            """);

        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);

        Assert.False(scan.Succeeded);
        Assert.Empty(scan.ActorMethods);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("request", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("hotfix", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("PingRequest", StringComparison.Ordinal) &&
            diagnostic.Contains("behavior", StringComparison.OrdinalIgnoreCase));
    }

    private static class StartupScanFixture
    {
        public sealed class RoomActor : IActor
        {
        }

        public static class HotfixStartup
        {
            public static void ConfigureActors(ActorHostBuilder actors)
            {
                actors.RegisterStartup(
                    "matchmaking",
                    static _ => ActorStartupPlan.Create<RoomActor>(ActorId.From("default")));
                actors.RegisterPlacement<RoomActor, ActorId>(
                    static context => context.Candidates[0]);
            }

            public static void ConfigureServices(IServiceCollection services)
            {
                services.AddSingleton<IMarkerService, MarkerService>();
            }
        }

        public interface IMarkerService
        {
        }

        public sealed class MarkerService : IMarkerService
        {
        }
    }

    private static class NonStaticStartupFixture
    {
        public sealed class HotfixStartup
        {
            public static void ConfigureActors(ActorHostBuilder actors)
            {
                actors.RegisterStartup("matchmaking", static _ => ActorStartupPlan.Empty);
            }
        }
    }

    private static class InvalidStartupSignatureFixture
    {
        public static class HotfixStartup
        {
            public static void ConfigureActors()
            {
            }
        }
    }

    [Fact]
    public void Scanner_rejects_hotfix_local_actor_result_dto()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingRequest(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            public sealed record PingReply(string Text);

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
                public static ValueTask<PingReply> PingAsync(
                    this UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<PingReply>(new PingReply(request.Text));
                }
            }
            """);

        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);

        Assert.False(scan.Succeeded);
        Assert.Empty(scan.ActorMethods);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("result", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("hotfix", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("PingReply", StringComparison.Ordinal) &&
            diagnostic.Contains("behavior", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_rejects_hotfix_local_actor_dto_nested_in_closed_generic()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using System.Collections.Generic;
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingReply(string Text);
            """,
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            public sealed record PingRequest(string Text);

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
                public static ValueTask<PingReply> PingAsync(
                    this UserActor self,
                    List<PingRequest> request)
                {
                    return new ValueTask<PingReply>(new PingReply(request[0].Text));
                }
            }
            """);

        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);

        Assert.False(scan.Succeeded);
        Assert.Empty(scan.ActorMethods);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("request", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("hotfix", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("System.Collections.Generic.List", StringComparison.Ordinal) &&
            diagnostic.Contains("behavior", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_does_not_treat_spoofed_lakona_actor_type_as_actor_api()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            namespace Lakona.Game.Server.Actors
            {
                public interface IActor
                {
                }

                public abstract class Actor : IActor
                {
                }

                public abstract class Actor<TKey> : Actor
                {
                }
            }

            namespace StableGame
            {
                public sealed class UserActor : Lakona.Game.Server.Actors.Actor<string>
                {
                }
            }
            """,
            """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            public sealed record HotfixLocalRequest(string Text);

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
                public static ValueTask PingAsync(this UserActor self, HotfixLocalRequest request)
                {
                    return default;
                }
            }
            """);

        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        Assert.Empty(scan.ActorMethods);
        Assert.Single(scan.Methods);
    }

    [Fact]
    public void Scanner_actor_api_descriptors_do_not_expose_generation_local_dispatch_references()
    {
        var fixture = TwoAssemblyHotfixFixture.Create(
            """
            using Lakona.Game.Server.Actors;

            namespace StableGame;

            public sealed class UserActor : Actor<string>
            {
            }

            public sealed record PingRequest(string Text);

            public sealed record PingReply(string Text);
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using StableGame;

            namespace HotfixGame;

            [HotfixBehaviorOf(typeof(UserActor))]
            public static partial class UserBehavior
            {
                public static ValueTask<PingReply> PingAsync(
                    this UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<PingReply>(new PingReply(request.Text));
                }
            }
            """);

        var scan = HotfixBehaviorScanner.Scan(fixture.HotfixAssembly);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var descriptor = Assert.Single(scan.ActorMethods);
        var exposedGenerationLocalReferences = descriptor.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property =>
                property.PropertyType == typeof(MethodInfo) ||
                typeof(Delegate).IsAssignableFrom(property.PropertyType) ||
                property.Name.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Selector", StringComparison.OrdinalIgnoreCase))
            .Select(static property => property.Name);
        Assert.Empty(exposedGenerationLocalReferences);
    }

    [Fact]
    public void Scan_discovers_hotfix_behavior_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_discovers_hotfix_behavior_methods), module =>
        {
            CreateHotfixBehaviorType(module, "ChatRoomBehavior", typeof(ChatRoomActor), methodName: "JoinAsync");
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Assert.Single(result.Methods);
        Assert.Equal(typeof(ChatRoomActor).FullName, method.Key.StateTypeName);
        Assert.Equal("JoinAsync", method.Key.MethodName);
        Assert.Equal(typeof(int).FullName, method.Key.ReturnTypeName);
        Assert.Equal([typeof(int).FullName!], method.Key.ParameterTypeNames);
    }

    [Fact]
    public void Scan_reports_behavior_name_for_non_static_behavior_type()
    {
        var assembly = CreateAssembly(nameof(Scan_reports_behavior_name_for_non_static_behavior_type), module =>
        {
            DefineBehaviorType(
                    module,
                    "InvalidChatRoomBehavior",
                    typeof(ChatRoomActor),
                    TypeAttributes.Public | TypeAttributes.Class)
                .CreateType();
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Hotfix behavior", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_duplicate_method_keys()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_duplicate_method_keys), module =>
        {
            CreateHotfixBehaviorType(module, "DuplicateStateBehaviorA", typeof(DuplicateState), methodName: "Add");
            CreateHotfixBehaviorType(module, "DuplicateStateBehaviorB", typeof(DuplicateState), methodName: "Add");
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("Duplicate hotfix method key", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_generic_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_generic_extension_methods), module =>
        {
            CreateGenericHotfixBehaviorType(module, typeof(GenericState));
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("must not be generic", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_rejects_out_parameter_extension_methods()
    {
        var assembly = CreateAssembly(nameof(Scan_rejects_out_parameter_extension_methods), module =>
        {
            CreateOutParameterHotfixBehaviorType(module, typeof(OutParameterState));
        });

        var result = HotfixBehaviorScanner.Scan(assembly);

        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("must not use by-ref, out, or pointer parameter types", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_behavior_extension_methods_that_do_not_target_actor_state()
    {
        var result = HotfixBehaviorScanner.Scan(typeof(GeneratedWrapperIgnoredBehavior).Assembly, [typeof(GeneratedWrapperIgnoredBehavior)]);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains(
                "GeneratedWrapperIgnoredBehavior.PingAsync",
                StringComparison.Ordinal));
        Assert.Contains(result.Methods, binding => binding.Method.Name == nameof(GeneratedWrapperIgnoredBehavior.ActorPingAsync));
    }

    [Fact]
    public void Dispatch_table_rejects_null_methods()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new HotfixDispatchTable(1, null!));

        Assert.Equal("methods", exception.ParamName);
    }

    [Fact]
    public void Dispatch_table_rejects_null_bindings()
    {
        var exception = Assert.Throws<ArgumentException>(() => new HotfixDispatchTable(1, [null!]));

        Assert.Equal("methods", exception.ParamName);
    }

    [Fact]
    public void Scanner_requires_one_hotfix_service_for_declared_rpc_contract()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(MissingHotfixContract).Assembly,
            [typeof(UnrelatedHotfixService)],
            requiredServiceContracts: [typeof(MissingHotfixContract)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("MissingHotfixContract", StringComparison.Ordinal) &&
            diagnostic.Contains("exactly one", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_discovers_hotfix_lifecycle_methods()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestLifecycleContract).Assembly,
            [typeof(TestLifecycleImplementation)],
            requiredServiceContracts: [typeof(TestLifecycleContract)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(typeof(TestLifecycleContract), binding.ContractType);
    }

    [Fact]
    public void Scanner_rejects_lifecycle_methods_that_use_service_call_wrapper()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestLifecycleContract).Assembly,
            [typeof(TestLifecycleWithServiceCallImplementation)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(TestLifecycleWithServiceCallImplementation), StringComparison.Ordinal) &&
            diagnostic.Contains("HotfixLifecycleCall<TRequest>", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_service_methods_that_use_lifecycle_call_wrapper()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestServiceContract).Assembly,
            [typeof(TestServiceWithLifecycleCallImplementation)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(TestServiceWithLifecycleCallImplementation), StringComparison.Ordinal) &&
            diagnostic.Contains("HotfixServiceCall<TRequest>", StringComparison.Ordinal) &&
            diagnostic.Contains("HotfixServiceCall<TRequest, TCallback>", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_reports_required_lifecycle_contracts_with_lifecycle_diagnostic_wording()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(TestLifecycleContract).Assembly,
            [],
            requiredServiceContracts: [typeof(TestLifecycleContract)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(TestLifecycleContract), StringComparison.Ordinal) &&
            diagnostic.Contains("[HotfixService] or [HotfixLifecycle]", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_duplicate_hotfix_services_for_declared_rpc_contract()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(DuplicateHotfixContract).Assembly,
            [typeof(DuplicateHotfixServiceA), typeof(DuplicateHotfixServiceB)],
            requiredServiceContracts: [typeof(DuplicateHotfixContract)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("DuplicateHotfixContract", StringComparison.Ordinal) &&
            diagnostic.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_accepts_non_static_service_with_constructor_dependencies()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ConstructorDependencyServiceContract).Assembly,
            [typeof(ConstructorDependencyService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(typeof(ConstructorDependencyService), binding.ServiceType);
    }

    [Fact]
    public void Scanner_rejects_non_static_service_with_raw_dto_parameter()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ConstructorDependencyServiceContract).Assembly,
            [typeof(InstanceRawDtoService)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(InstanceRawDtoService), StringComparison.Ordinal) &&
            diagnostic.Contains("instance dispatch", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Contains("HotfixServiceCall", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_accepts_static_service_with_raw_dto_parameter()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(ConstructorDependencyServiceContract).Assembly,
            [typeof(StaticRawDtoService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        var binding = Assert.Single(scan.Services);
        Assert.Equal(typeof(StaticRawDtoService), binding.ServiceType);
    }

    [Fact]
    public void Scanner_rejects_open_generic_hotfix_service_type()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(GenericHotfixService<>).Assembly,
            [typeof(GenericHotfixService<>)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(GenericHotfixService<object>), StringComparison.Ordinal) ||
            diagnostic.Contains("open generic", StringComparison.Ordinal));
    }

    [Fact]
    public void Scanner_rejects_service_with_multiple_unmarked_public_constructors()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(MultipleConstructorService).Assembly,
            [typeof(MultipleConstructorService)]);

        Assert.False(scan.Succeeded);
        Assert.Contains(scan.Diagnostics, diagnostic =>
            diagnostic.Contains("multiple public constructors", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scanner_accepts_service_with_activator_utilities_constructor_marker()
    {
        var scan = HotfixBehaviorScanner.Scan(
            typeof(MarkedConstructorService).Assembly,
            [typeof(MarkedConstructorService)]);

        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
    }

    private static Assembly CreateAssembly(string name, Action<ModuleBuilder> build)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"{name}_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule($"{name}Module");

        build(module);

        return assembly;
    }

    private static Type CreateHotfixBehaviorType(ModuleBuilder module, string typeName, Type stateType, string methodName)
    {
        var behaviorType = DefineBehaviorType(module, typeName, stateType);
        var method = DefineExtensionMethod(behaviorType, methodName, typeof(int), [stateType, typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        return behaviorType.CreateType();
    }

    private static Type CreateGenericHotfixBehaviorType(ModuleBuilder module, Type stateType)
    {
        var behaviorType = DefineBehaviorType(module, "GenericStateBehavior", stateType);
        var method = behaviorType.DefineMethod(
            "Generic",
            MethodAttributes.Public | MethodAttributes.Static);
        var genericParameter = method.DefineGenericParameters("T")[0];
        method.SetReturnType(genericParameter);
        method.SetParameters(stateType, genericParameter);
        AddExtensionAttribute(method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        return behaviorType.CreateType();
    }

    private static Type CreateOutParameterHotfixBehaviorType(ModuleBuilder module, Type stateType)
    {
        var behaviorType = DefineBehaviorType(module, "OutParameterStateBehavior", stateType);
        var method = DefineExtensionMethod(behaviorType, "TryRead", typeof(bool), [stateType, typeof(int).MakeByRefType()]);
        method.DefineParameter(2, ParameterAttributes.Out, "value");

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        return behaviorType.CreateType();
    }

    private static TypeBuilder DefineBehaviorType(
        ModuleBuilder module,
        string typeName,
        Type stateType,
        TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class)
    {
        var behaviorType = module.DefineType(
            typeName,
            attributes);

        var attributeConstructor = typeof(HotfixBehaviorOfAttribute).GetConstructor([typeof(Type)])!;
        behaviorType.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, [stateType]));

        return behaviorType;
    }

    private static MethodBuilder DefineExtensionMethod(TypeBuilder behaviorType, string name, Type returnType, Type[] parameterTypes)
    {
        var method = behaviorType.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            parameterTypes);
        AddExtensionAttribute(method);

        return method;
    }

    private static void AddExtensionAttribute(MethodBuilder method)
    {
        var attributeConstructor = typeof(System.Runtime.CompilerServices.ExtensionAttribute).GetConstructor(Type.EmptyTypes)!;
        method.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, []));
    }

    private sealed record TwoAssemblyHotfixFixture(Assembly StableAssembly, Assembly HotfixAssembly)
    {
        public static TwoAssemblyHotfixFixture Create(string stableSource, string hotfixSource)
        {
            var references = CreateDefaultReferences();
            var stableAssemblyName = "StableGame_" + Guid.NewGuid().ToString("N");
            var hotfixAssemblyName = "HotfixGame_" + Guid.NewGuid().ToString("N");
            var stableBytes = Compile(stableAssemblyName, stableSource, references);
            var stableAssembly = Assembly.Load(stableBytes);
            var hotfixReferences = references
                .Concat([MetadataReference.CreateFromImage(stableBytes)])
                .ToArray();
            var hotfixBytes = Compile(hotfixAssemblyName, hotfixSource, hotfixReferences);

            return new TwoAssemblyHotfixFixture(stableAssembly, Assembly.Load(hotfixBytes));
        }

        private static byte[] Compile(
            string assemblyName,
            string source,
            IReadOnlyList<MetadataReference> references)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
            }

            return stream.ToArray();
        }

        private static MetadataReference[] CreateDefaultReferences()
        {
            return HotfixTestMetadataReferences.CreateDefaultReferences(
                typeof(Actor<>),
                typeof(HotfixBehaviorOfAttribute),
                typeof(ValueTask),
                typeof(CancellationToken));
        }
    }

    public sealed class ChatRoomActor
    {
    }

    public sealed class DuplicateState
    {
    }

    public sealed class GenericState
    {
    }

    public sealed class OutParameterState
    {
    }

    [RpcService(201)]
    public interface MissingHotfixContract
    {
        [RpcMethod(1)]
        ValueTask PingAsync(MissingHotfixRequest request);
    }

    public sealed class MissingHotfixRequest
    {
    }

    public interface TestLifecycleContract
    {
        [RpcMethod(301)]
        ValueTask ExpiredAsync(TestLifecycleRequest request);
    }

    public sealed class TestLifecycleRequest
    {
    }

    public interface TestServiceContract
    {
        [RpcMethod(302)]
        ValueTask PingAsync(TestServiceRequest request);
    }

    public sealed class TestServiceRequest
    {
    }

    [HotfixLifecycle(typeof(TestLifecycleContract))]
    public sealed class TestLifecycleImplementation
    {
        public static ValueTask ExpiredAsync(HotfixLifecycleCall<TestLifecycleRequest> call)
        {
            return default;
        }
    }

    [HotfixLifecycle(typeof(TestLifecycleContract))]
    public sealed class TestLifecycleWithServiceCallImplementation
    {
        public static ValueTask ExpiredAsync(HotfixServiceCall<TestLifecycleRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(TestServiceContract))]
    public sealed class TestServiceWithLifecycleCallImplementation
    {
        public static ValueTask PingAsync(HotfixLifecycleCall<TestServiceRequest> call)
        {
            return default;
        }
    }

    public sealed class UnrelatedHotfixService
    {
    }

    [RpcService(202)]
    public interface DuplicateHotfixContract
    {
        [RpcMethod(1)]
        ValueTask PingAsync(DuplicateHotfixRequest request);
    }

    public sealed class DuplicateHotfixRequest
    {
    }

    [HotfixService(typeof(DuplicateHotfixContract))]
    public sealed class DuplicateHotfixServiceA
    {
        public static ValueTask PingAsync(HotfixServiceCall<DuplicateHotfixRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(DuplicateHotfixContract))]
    public sealed class DuplicateHotfixServiceB
    {
        public static ValueTask PingAsync(HotfixServiceCall<DuplicateHotfixRequest> call)
        {
            return default;
        }
    }

    [RpcService(303)]
    public interface ConstructorDependencyServiceContract
    {
        [RpcMethod(1)]
        ValueTask PingAsync(ConstructorDependencyRequest request);
    }

    public sealed class ConstructorDependencyRequest
    {
    }

    public sealed class ConstructorDependency
    {
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class ConstructorDependencyService
    {
        public ConstructorDependencyService(ConstructorDependency dependency)
        {
        }

        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class InstanceRawDtoService
    {
        public ValueTask PingAsync(ConstructorDependencyRequest request)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class StaticRawDtoService
    {
        public static ValueTask PingAsync(ConstructorDependencyRequest request)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class GenericHotfixService<T>
    {
        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class MultipleConstructorService
    {
        public MultipleConstructorService(ConstructorDependency dependency)
        {
        }

        public MultipleConstructorService(string value)
        {
        }

        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }

    [HotfixService(typeof(ConstructorDependencyServiceContract))]
    public sealed class MarkedConstructorService
    {
        public MarkedConstructorService(string value)
        {
        }

        [ActivatorUtilitiesConstructor]
        public MarkedConstructorService(ConstructorDependency dependency)
        {
        }

        public ValueTask PingAsync(HotfixServiceCall<ConstructorDependencyRequest> call)
        {
            return default;
        }
    }
}

public sealed class GeneratedWrapperIgnoredState
{
}

public readonly struct GeneratedWrapperIgnoredRef
{
}

[HotfixBehaviorOf(typeof(GeneratedWrapperIgnoredState))]
public static partial class GeneratedWrapperIgnoredBehavior
{
    public static int ActorPingAsync(this GeneratedWrapperIgnoredState self)
    {
        return 1;
    }

    public static int PingAsync(this GeneratedWrapperIgnoredRef self)
    {
        return 2;
    }
}
