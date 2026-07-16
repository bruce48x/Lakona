using Lakona.Game.Abstractions;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Sessions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixGeneratorTests
{
    private static readonly string ForbiddenGameEndpointType = string.Concat("Game", "Endpoint", "Name");

    [Fact]
    public void Generator_emits_typed_entries_for_instance_timer_callbacks()
    {
        var result = GeneratorTestHost.Run("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;

            namespace Game.Hotfix;

            public sealed record SweepArgs(int BatchSize);

            [HotfixTimer]
            public sealed partial class SweepTimer
            {
                public ValueTask SweepAsync(TimerTick<SweepArgs> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            """);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public static class Entries", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("HotfixTimerEntry<global::Game.Hotfix.SweepArgs>", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("public static", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("SweepAsync", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_rejects_static_timer_callbacks()
    {
        var result = GeneratorTestHost.Run("""
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Game.Server.Hotfix.Abstractions.Timers;

            [HotfixTimer]
            public sealed partial class SweepTimer
            {
                public static ValueTask SweepAsync(TimerTick<int> tick)
                {
                    _ = tick;
                    return default;
                }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "LKNHOTFIX034");
    }

    [Fact]
    public void Generator_uses_explicit_actor_wire_name_for_routes_and_remote_invocation()
    {
        var appSource = """
            using Lakona.Game.Server.Actors;

            namespace Game.Server;

            public sealed class PingRequest { }

            [ActorName("battle-room")]
            public sealed class BattleRoomActor : Actor<string>
            {
            }
            """;
        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(BattleRoomActor))]
            public sealed partial class BattleRoomBehavior
            {
                public ValueTask PingAsync(BattleRoomActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("\"battle-room\"", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"battleRoom\"", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_and_sample_hotfix_code_does_not_bypass_scoped_service_accessor()
    {
        var root = FindRepositoryRoot();
        var offenders = new[] { "src", "samples" }
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(static path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(static path => !IsBuildOutputPath(path))
            .Select(static path => new
            {
                Path = path,
                Text = File.ReadAllText(path)
            })
            .Where(static file => file.Text.Contains(".Current.Services", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Generator_emits_hotfix_actor_refs_from_behavior_methods_without_contract()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class LoginRequest { public string Password { get; set; } = ""; }
            public sealed class LoginReply { public bool Accepted { get; set; } }
            public sealed class TouchRequest { }

            public sealed class UserActor : Actor<UserId>
            {
                internal int LoginCount;
            }
            """;

        var hotfixSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<LoginReply> LoginAsync(
                    UserActor self,
                    LoginRequest request,
                    CancellationToken cancellationToken = default)
                {
                    return new ValueTask<LoginReply>(new LoginReply { Accepted = true });
                }

                public ValueTask TouchAsync(
                    UserActor self,
                    TouchRequest request,
                    CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }

            [HotfixStartup]
            public static class Startup
            {
                [HotfixConfigureActors]
                public static void ConfigureActors(ActorHostBuilder actors)
                {
                    actors.RegisterStartup<UserActor, string>(static context => context.Candidates[0]);
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        var generated = result.Hotfix.GeneratedSource;

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.DoesNotContain("UserActors", result.App.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActors", generated, StringComparison.Ordinal);
        Assert.Contains("public sealed class ActorAccess", generated, StringComparison.Ordinal);
        Assert.Contains("public LocalActor<TActor> Local<TActor>(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.Contains("public ActorRoute<TActor> Route<TActor>(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.Contains("public ActorPlacement<TActor, global::Game.Server.UserId> Place<TActor>(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.Contains("public StartupActor<TActor, string> Startup<TActor>(string key)", generated, StringComparison.Ordinal);
        Assert.Contains("IStartupActorInvoker", generated, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorPlacementCreateMode.Ensure", generated, StringComparison.Ordinal);
        Assert.Contains("public readonly struct ActorRoute<TActor>", generated, StringComparison.Ordinal);
        Assert.Contains("public readonly struct LocalActor<TActor>", generated, StringComparison.Ordinal);
        Assert.Contains("private readonly ActorAccess _actors;", generated, StringComparison.Ordinal);
        Assert.Contains("IHotfixRuntimeAccessor HotfixRuntime", generated, StringComparison.Ordinal);
        Assert.Contains("HotfixActorMailboxDispatch", generated, StringComparison.Ordinal);
        Assert.Contains("runtimeAccessor,", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("dynamic", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("object _inner", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", generated, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest, TResult> method", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", generated, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest> method", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public UserRef Get(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public UserRemoteRef Remote(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static global::System.Threading.Tasks.ValueTask<global::Game.Server.LoginReply> LoginAsync(this global::Game.Server.UserRef self", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryLoginAsync", generated, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.HotfixActorApiMetadata.ActorMessageKind", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[global::Lakona.Game.Server.Hotfix.HotfixActorApiMetadata.MethodKeyKey]", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("metadata);", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat("Hotfix", "Actor", "Contract"), result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActorClusterHandler", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_actor_access_supports_actor_only_type_arguments_and_method_group_inference()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class LoginRequest { }
            public sealed class LoginReply { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<LoginReply> LoginAsync(UserActor self, LoginRequest request)
                {
                    return new ValueTask<LoginReply>(new LoginReply());
                }
            }

            internal sealed class Caller(ActorAccess actors)
            {
                public ValueTask<LoginReply> LoginAsync(UserId userId)
                {
                    return actors.Route<UserActor>(userId).CallAsync(UserBehavior.Entries.LoginAsync, new LoginRequest());
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
    }

    [Fact]
    public void Generated_actor_access_rejects_an_actor_key_mismatch_at_compile_time()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public readonly record struct RoomId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            public sealed class RoomActor : Actor<RoomId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request) => default;
            }

            [HotfixBehaviorOf(typeof(RoomActor))]
            public sealed partial class RoomBehavior
            {
                public ValueTask PingAsync(RoomActor self, PingRequest request) => default;
            }

            internal sealed class Caller(ActorAccess actors)
            {
                public void Invalid(RoomId roomId)
                {
                    _ = actors.Route<UserActor>(roomId);
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Contains(result.Hotfix.ErrorDiagnostics, diagnostic => diagnostic.Id == "CS0311");
    }

    [Fact]
    public void Generator_emits_internal_hotfix_actor_refs_for_internal_actor_types()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public sealed class PingRequest { }

            internal sealed class UserActor : Actor<string>
            {
            }
            """;

        var hotfixSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            internal sealed partial class UserBehavior
            {
                public ValueTask PingAsync(
                    UserActor self,
                    PingRequest request,
                    CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        var generated = result.Hotfix.GeneratedSource;

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("public sealed class ActorAccess", generated, StringComparison.Ordinal);
        Assert.Contains("internal ActorRoute<TActor> Route<TActor>(string id)", generated, StringComparison.Ordinal);
        Assert.Contains("internal LocalActor<TActor> Local<TActor>(string id)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActors", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UserRouteRef", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UserLocalRef", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_public_behavior_method_with_hotfix_local_request_type()
    {
        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            public sealed class LocalRequest { }

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, LocalRequest request)
                {
                    return default;
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        AssertContainsBehaviorDiagnostic(
            result.Hotfix.GeneratorDiagnostics,
            "PingAsync",
            "request",
            "hotfix",
            expectedId: "LKNHOTFIX027");
    }

    [Fact]
    public void Generator_reports_public_behavior_method_with_no_request_dto()
    {
        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self)
                {
                    return default;
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        AssertContainsBehaviorDiagnostic(
            result.Hotfix.GeneratorDiagnostics,
            "PingAsync",
            "request",
            "one");
    }

    [Fact]
    public void Generator_reports_public_behavior_method_with_two_non_cancellation_parameters()
    {
        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request, PingRequest other)
                {
                    return default;
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        AssertContainsBehaviorDiagnostic(
            result.Hotfix.GeneratorDiagnostics,
            "PingAsync",
            "request",
            "one");
    }

    [Theory]
    [InlineData("Task", "return Task.CompletedTask;", "return")]
    [InlineData("void", "", "return")]
    [InlineData("ValueTask<LocalReply>", "return new ValueTask<LocalReply>(new LocalReply());", "result")]
    public void Generator_reports_public_behavior_method_with_unsupported_return_type(
        string returnType,
        string returnStatement,
        string expectedMessage)
    {
        var hotfixSource = $$"""
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            public sealed class LocalReply { }

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public {{returnType}} PingAsync(UserActor self, PingRequest request)
                {
                    {{returnStatement}}
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        AssertContainsBehaviorDiagnostic(
            result.Hotfix.GeneratorDiagnostics,
            "PingAsync",
            "return",
            expectedMessage);
    }

    [Fact]
    public void Generator_reports_duplicate_canonical_behavior_method_key()
    {
        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }

                public ValueTask PingAsync(UserActor self, PingRequest request, System.Threading.CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        AssertContainsBehaviorDiagnostic(
            result.Hotfix.GeneratorDiagnostics,
            "PingAsync",
            "duplicate",
            "canonical");
    }

    [Fact]
    public void Generator_reports_duplicate_generated_behavior_method_signature()
    {
        var hotfixSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }

                public ValueTask<PingReply> PingAsync(UserActor self, PingRequest request, CancellationToken cancellationToken = default)
                {
                    return new ValueTask<PingReply>(new PingReply());
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        AssertContainsBehaviorDiagnostic(
            result.Hotfix.GeneratorDiagnostics,
            "PingAsync",
            "duplicate",
            "generated");
        Assert.Contains(result.Hotfix.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX030");
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_distinct_behavior_methods_for_overloads_with_different_request_dtos()
    {
        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }

                public ValueTask PingAsync(UserActor self, TouchRequest request)
                {
                    return default;
                }
            }
            """;

        var result = RunBehaviorFirstHotfix(hotfixSource);

        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("|method:PingAsync|request:Game.Server.PingRequest, Game.Server|", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("|method:PingAsync|request:Game.Server.TouchRequest, Game.Server|", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("requestType == typeof(global::Game.Server.PingRequest)", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("requestType == typeof(global::Game.Server.TouchRequest)", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_uses_runtime_type_full_name_for_nested_stable_dto_identity()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);

            public static class Outer
            {
                public sealed class NestedDto { }
                public sealed class NestedReply { }
            }

            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<Outer.NestedReply> NestedAsync(UserActor self, Outer.NestedDto request)
                {
                    return new ValueTask<Outer.NestedReply>(new Outer.NestedReply());
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("request:Game.Server.Outer+NestedDto, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("result:Game.Server.Outer+NestedReply, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("request:Game.Server.Outer.NestedDto", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Game.Server.Outer.NestedDto, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_uses_runtime_type_full_name_for_closed_generic_stable_dto_identity()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class Box<T> { }
            public sealed class PingRequest { }
            public sealed class TouchRequest { }
            public sealed class PingReply { }
            public sealed class TouchReply { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<Box<PingReply>> PingAsync(UserActor self, Box<PingRequest> request)
                {
                    return new ValueTask<Box<PingReply>>(new Box<PingReply>());
                }

                public ValueTask<Box<TouchReply>> TouchAsync(UserActor self, Box<TouchRequest> request)
                {
                    return new ValueTask<Box<TouchReply>>(new Box<TouchReply>());
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("request:Game.Server.Box`1[[Game.Server.PingRequest, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("request:Game.Server.Box`1[[Game.Server.TouchRequest, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("result:Game.Server.Box`1[[Game.Server.PingReply, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("result:Game.Server.Box`1[[Game.Server.TouchReply, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("request:Game.Server.Box`1, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("result:Game.Server.Box`1, Game.Server", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_emit_stable_actor_refs_when_actor_is_referenced_from_another_assembly()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.DoesNotContain("public sealed class ActorAccess", result.App.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class ActorAccess", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("public ActorRoute<TActor> Route<TActor>(global::Game.Server.UserId id)", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActors", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_behavior_owned_extensions_for_actor_refs()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class LoginRequest { }
            public sealed class LoginReply { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Users;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask<LoginReply> LoginAsync(UserActor self, LoginRequest request, CancellationToken cancellationToken = default)
                {
                    return new ValueTask<LoginReply>(new LoginReply());
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        var generated = result.Hotfix.GeneratedSource;

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("namespace Game.Hotfix.Users", generated, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class UserBehavior", generated, StringComparison.Ordinal);
        Assert.Contains("public sealed class ActorAccess", generated, StringComparison.Ordinal);
        Assert.Contains("public LocalActor<TActor> Local<TActor>(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.Contains("public ActorRoute<TActor> Route<TActor>(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask<TResult> CallAsync<TRequest, TResult>(", generated, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest, TResult> method", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask CallAsync<TRequest>(", generated, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.Abstractions.Actors.HotfixActorEntry<TActor, TRequest> method", generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask PostAsync<TRequest>(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public UserRef Get(global::Game.Server.UserId id)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public UserRemoteRef Remote(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static global::System.Threading.Tasks.ValueTask<global::Game.Server.LoginReply> LoginAsync(this global::Game.Server.UserRef self", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryLoginAsync", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_local_and_route_actor_refs_without_business_wrappers()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed class PingRequest { }
            public sealed class RoomActor : Actor<RoomId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Rooms;

            [HotfixBehaviorOf(typeof(RoomActor))]
            internal sealed partial class RoomBehavior
            {
                public ValueTask PingAsync(RoomActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        var generated = result.Hotfix.GeneratedSource;

        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("internal sealed partial class RoomBehavior", generated, StringComparison.Ordinal);
        Assert.Contains("public readonly struct LocalActor<TActor>", generated, StringComparison.Ordinal);
        Assert.Contains("public readonly struct ActorRoute<TActor>", generated, StringComparison.Ordinal);
        Assert.Contains("HotfixActorEntry<TActor, TRequest, TResult> method", generated, StringComparison.Ordinal);
        Assert.Contains("CallCoreAsync<TRequest, TResult>(", generated, StringComparison.Ordinal);
        Assert.Contains("CallCoreAsync<TRequest>(", generated, StringComparison.Ordinal);
        Assert.Contains("PostCoreAsync<TRequest>(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RoomActors", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RoomLocalRef", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RoomRouteRef", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static global::System.Threading.Tasks.ValueTask PingAsync(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryPingAsync", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_emit_static_delegate_or_method_info_actor_call_caches()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public sealed class RoomActor : Actor<string> { }
            """;

        var hotfixSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(RoomActor))]
            public sealed partial class RoomBehavior
            {
                public ValueTask<int> JoinAsync(
                    RoomActor self,
                    int request,
                    CancellationToken cancellationToken = default)
                {
                    return new ValueTask<int>(request + 1);
                }

                public ValueTask RunTickAsync(
                    RoomActor self,
                    int request,
                    CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        var generated = result.Hotfix.GeneratedSource;
        var staticReadonly = string.Concat("static ", "readonly ");
        var delegateCache = string.Concat(staticReadonly, "global::System.", "Delegate");
        var methodInfoCache = string.Concat(staticReadonly, "global::System.Reflection.", "MethodInfo");
        var runtimeHandleText = string.Concat("Runtime", "Method", "Handle");
        var handleText = string.Concat("Method", "Handle");

        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.DoesNotContain(delegateCache, generated, StringComparison.Ordinal);
        Assert.DoesNotContain(methodInfoCache, generated, StringComparison.Ordinal);
        Assert.DoesNotContain(runtimeHandleText, generated, StringComparison.Ordinal);
        Assert.DoesNotContain(handleText, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_rejects_behavior_method_group_from_wrong_actor()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct RoomId(string Value);
            public sealed class PingRequest { }
            public sealed class RoomActor : Actor<RoomId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Rooms;

            [HotfixBehaviorOf(typeof(RoomActor))]
            public sealed partial class RoomBehavior
            {
                public ValueTask PingAsync(RoomActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Contains(
            "throw new global::System.ArgumentException(\"The supplied behavior method is not a generated actor behavior method for RoomActor.\", nameof(method));",
            result.Hotfix.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_duplicate_hotfix_behaviors_without_throwing()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Users;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }
            }

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserSessionBehavior
            {
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        Assert.Contains(result.Hotfix.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX018");
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_nested_hotfix_behavior_without_generating_actor_ref_extensions()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Users;

            public partial class UserModule
            {
                [HotfixBehaviorOf(typeof(UserActor))]
                private static partial class UserBehavior
                {
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        Assert.Contains(result.Hotfix.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX021");
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_file_local_hotfix_behavior_without_generating_actor_ref_extensions()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Users;

            [HotfixBehaviorOf(typeof(UserActor))]
            file static partial class UserBehavior
            {
                public static ValueTask PingAsync(this UserActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        Assert.Contains(result.Hotfix.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX021");
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_non_static_hotfix_behavior_without_generating_actor_ref_extensions()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix.Users;

            [HotfixBehaviorOf(typeof(UserActor))]
            public partial class UserBehavior
            {
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(appSource, hotfixSource, appAssemblyName: "Game.Server", hotfixAssemblyName: "Game.Hotfix");

        Assert.Contains(result.Hotfix.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX021");
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    private static string SharedChatServiceSource()
    {
        return """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            namespace Shared.Contracts.Chat
            {
                public static class RpcContractIds
                {
                    public const int ChatService = 1;
                    public const int Bind = 7;
                }

                public sealed class ChatBindRequest
                {
                }

                public interface IChatCallback
                {
                }

                [RpcService(RpcContractIds.ChatService, NotificationContract = typeof(IChatCallback))]
                public interface IChatService
                {
                    [RpcMethod(RpcContractIds.Bind)]
                    ValueTask BindAsync(ChatBindRequest req);
                }
            }

            namespace Server.App.Generated
            {
                using System;
                using Lakona.Rpc.Server;
                using Shared.Contracts.Chat;

                public sealed class ChatCallbackProxy : IChatCallback
                {
                    public ChatCallbackProxy(RpcSession session)
                    {
                    }
                }

                public static class ChatServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, IChatService> implFactory)
                    {
                    }
                }
            }
            """;
    }

    private static TwoPhaseGeneratorRunResult RunBehaviorFirstHotfix(string hotfixSource)
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class TouchRequest { }
            public sealed class PingReply { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        return GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");
    }

    private static void AssertContainsBehaviorDiagnostic(
        IReadOnlyList<Diagnostic> diagnostics,
        string methodName,
        params string[] messageFragments)
    {
        AssertContainsBehaviorDiagnostic(diagnostics, methodName, messageFragments, expectedId: null);
    }

    private static void AssertContainsBehaviorDiagnostic(
        IReadOnlyList<Diagnostic> diagnostics,
        string methodName,
        string firstMessageFragment,
        string secondMessageFragment,
        string? expectedId = null)
    {
        AssertContainsBehaviorDiagnostic(
            diagnostics,
            methodName,
            new[] { firstMessageFragment, secondMessageFragment },
            expectedId);
    }

    private static void AssertContainsBehaviorDiagnostic(
        IReadOnlyList<Diagnostic> diagnostics,
        string methodName,
        string[] messageFragments,
        string? expectedId)
    {
        Assert.Contains(diagnostics, diagnostic =>
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                return false;
            }

            if (expectedId is not null && diagnostic.Id != expectedId)
            {
                return false;
            }

            var message = diagnostic.GetMessage();
            return message.Contains("behavior", StringComparison.OrdinalIgnoreCase)
                && message.Contains(methodName, StringComparison.Ordinal)
                && messageFragments.All(fragment => message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void AssertContainsNormalized(string expected, string actual)
    {
        Assert.Contains(expected.ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static bool IsBuildOutputPath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static part =>
                string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker after {startMarker}: {endMarker}");

        return source[start..end];
    }

    [Fact]
    public void Generator_emits_discoverable_public_actor_registration_provider()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using Lakona.Game.Server.Actors;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var emitted = GeneratorTestHost.RunHotfixWithGeneratedAppReferenceAndEmit(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix");

        Assert.Empty(emitted.Result.ErrorDiagnostics);
        Assert.Contains("public sealed class GeneratedHotfixActorRegistration", emitted.Result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration", emitted.Result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public interface IHotfixGeneratedServiceRegistration", emitted.Result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("TryAddSingleton<global::Lakona.Game.Server.Hotfix.ActorAccess>(services);", emitted.Result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActors", emitted.Result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IClusterMessageHandler", emitted.Result.GeneratedSource, StringComparison.Ordinal);

        var registration = emitted.Assembly.GetType("GeneratedHotfixActorRegistration", throwOnError: true);
        Assert.True(typeof(Lakona.Game.Server.Hotfix.IHotfixGeneratedServiceRegistration).IsAssignableFrom(registration));
    }

    [Fact]
    public void Generator_discovers_shared_rpc_service_contract_without_marker()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            namespace Shared.Contracts.Chat
            {
                public static class RpcContractIds
                {
                    public const int ChatService = 1;
                    public const int Bind = 7;
                }

                public sealed class ChatBindRequest
                {
                }

                public interface IChatCallback
                {
                }

                [RpcService(RpcContractIds.ChatService, NotificationContract = typeof(IChatCallback))]
                public interface IChatService
                {
                    [RpcMethod(RpcContractIds.Bind)]
                    ValueTask BindAsync(ChatBindRequest req);
                }
            }
            namespace Server.App.Generated
            {
                using System;
                using Lakona.Rpc.Server;
                using Shared.Contracts.Chat;

                public sealed class ChatCallbackProxy : IChatCallback
                {
                    public ChatCallbackProxy(RpcSession session)
                    {
                    }
                }

                public static class ChatServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, IChatService> implFactory)
                    {
                    }
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("internal sealed class ChatServiceProxy : global::Shared.Contracts.Chat.IChatService", result.GeneratedSource);
        Assert.Contains("public readonly struct ChatServiceCall<TRequest> : global::Lakona.Game.Server.Hotfix.IHotfixServiceCall<TRequest>", result.GeneratedSource);
        Assert.Contains("HotfixServiceCall<TRequest, global::Shared.Contracts.Chat.IChatCallback>", result.GeneratedSource);
        Assert.Contains("public global::Shared.Contracts.Chat.IChatCallback Callback => _inner.Callback;", result.GeneratedSource);
        Assert.Contains("global::Server.App.Generated.ChatServiceCall<global::Shared.Contracts.Chat.ChatBindRequest>", result.GeneratedSource);
        Assert.Contains("using var lease = _hotfixRuntime.AcquireCurrent();", result.GeneratedSource);
        Assert.Contains("var snapshot = lease.Snapshot;", result.GeneratedSource);
        Assert.DoesNotContain("_hotfixRuntime.Current", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<global::Lakona.Game.Server.Sessions.IGameSessionRegistry>(snapshot.Services)", result.GeneratedSource);
        Assert.Contains("GetCurrentSessionAsync(_connectionId, global::System.Threading.CancellationToken.None)", result.GeneratedSource);
        Assert.Contains("var currentSessionItems = currentSession is { } sessionKey", result.GeneratedSource);
        Assert.Contains("GetSessionItemsAsync(sessionKey, global::System.Threading.CancellationToken.None)", result.GeneratedSource);
        Assert.Contains("currentSession,", result.GeneratedSource);
        Assert.Contains("currentSessionItems,", result.GeneratedSource);
        Assert.Contains("snapshot.Services,", result.GeneratedSource);
        Assert.Contains("snapshot.Invoker.InvokeAsync", result.GeneratedSource);
        Assert.DoesNotContain("_hotfixServices.Current", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("global::Server.App.Generated.ChatServiceBinder.BindFactory", result.GeneratedSource);
        Assert.DoesNotContain("UseGeneratedHotfixServices", result.GeneratedSource);
        Assert.Contains("[global::Lakona.Game.Server.Hosting.LakonaRpcServiceAttribute(\"chat\")]", result.GeneratedSource);
        Assert.Contains("internal sealed class ChatServiceEndpointBinder : global::Lakona.Game.Server.Hosting.LakonaRpcServiceBinder", result.GeneratedSource);
        Assert.Contains("public override void Bind(global::Lakona.Game.Server.Hosting.LakonaGameServerRpcContext context)", result.GeneratedSource);
        Assert.DoesNotContain("return builder.BindServices", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HotfixRpcService", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(ForbiddenGameEndpointType, result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_synchronous_client_notification_admission_extensions()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            namespace Shared.Contracts.Chat
            {
                public sealed class ChatMessage
                {
                }

                [RpcNotificationContract(typeof(IChatService))]
                public interface IChatCallback
                {
                    [RpcNotification(8)]
                    void OnMessage(ChatMessage message);
                }

                [RpcService(1, NotificationContract = typeof(IChatCallback))]
                public interface IChatService
                {
                    [RpcMethod(7)]
                    ValueTask BindAsync(ChatMessage request);
                }
            }

            namespace Server.App.Generated
            {
                using System;
                using Lakona.Rpc.Server;
                using Shared.Contracts.Chat;

                public sealed class ChatCallbackProxy : IChatCallback
                {
                    public ChatCallbackProxy(RpcSession session)
                    {
                    }

                    public void OnMessage(ChatMessage message)
                    {
                    }
                }

                public static class ChatServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, IChatService> implFactory)
                    {
                    }
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public static global::Lakona.Game.Server.Sessions.ClientNotificationStatus OnMessage", result.GeneratedSource);
        Assert.Contains("return target.EnqueueGenerated(1, 8, \"OnMessage\", message);", result.GeneratedSource);
        Assert.DoesNotContain("OnMessage(this global::Lakona.Game.Server.Sessions.ClientNotificationTarget<global::Shared.Contracts.Chat.IChatCallback> target, global::Shared.Contracts.Chat.ChatMessage message, global::System.Threading.CancellationToken", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_skips_stable_rpc_service_output_when_role_disabled()
    {
        var result = GeneratorTestHost.Run(
            SharedChatServiceSource(),
            new Dictionary<string, string>
            {
                ["build_property.LakonaHotfixGenerateStableRpcServices"] = "false"
            });

        Assert.Empty(result.ErrorDiagnostics);
        Assert.DoesNotContain("ChatServiceProxy", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceEndpointBinder", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedHotfixRequiredServiceContracts", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceCall<global::Shared.Contracts.Chat.ChatBindRequest", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_keeps_stable_rpc_service_output_when_role_enabled()
    {
        var result = GeneratorTestHost.Run(
            SharedChatServiceSource(),
            new Dictionary<string, string>
            {
                ["build_property.LakonaHotfixGenerateStableRpcServices"] = "true"
            });

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("internal sealed class ChatServiceProxy : global::Shared.Contracts.Chat.IChatService", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ChatServiceEndpointBinder : global::Lakona.Game.Server.Hosting.LakonaRpcServiceBinder", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GeneratedHotfixRequiredServiceContracts", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_skips_current_compilation_stable_actor_refs_when_role_disabled()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.DoesNotContain("public sealed class UserActors", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public readonly struct UserRef", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public readonly struct UserLocalRef", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public readonly struct UserRemoteRef", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class UserActorClusterHandler", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed class GeneratedHotfixActorRegistration", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_excludes_actor_lifecycle_methods_from_actor_api()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            public readonly record struct QueueId(string Value);
            public sealed class QueueActor : Actor<QueueId> { }

            [HotfixBehaviorOf(typeof(QueueActor))]
            public sealed partial class QueueBehavior
            {
                [ActorStart]
                public ValueTask StartAsync(QueueActor self, ActorStartCall call)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.DoesNotContain(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "LKNHOTFIX028");
        Assert.Empty(result.ErrorDiagnostics);
        Assert.DoesNotContain("StartAsync", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_skips_app_side_actor_refs_when_role_disabled_but_keeps_hotfix_wrappers()
    {
        var appSource = """
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            [assembly: InternalsVisibleTo("Game.Hotfix")]

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            """;

        var hotfixSource = """
            using System.Threading.Tasks;
            using Game.Server;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixBehaviorOf(typeof(UserActor))]
            public sealed partial class UserBehavior
            {
                public ValueTask PingAsync(UserActor self, PingRequest request)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Game.Server",
            hotfixAssemblyName: "Game.Hotfix",
            hotfixGlobalOptions: new Dictionary<string, string>
            {
                ["build_property.LakonaHotfixGenerateStableRpcServices"] = "false"
            });

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.DoesNotContain("public sealed class ActorAccess", result.App.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class ActorAccess", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("public ActorRoute<TActor> Route<TActor>(global::Game.Server.UserId id)", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("UserActors", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("this global::Game.Server.UserRef self", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_compilation_does_not_emit_app_owned_service_proxies_when_referencing_generated_app()
    {
        var appSource = SharedChatServiceSource();
        var hotfixSource = """
            using System.Threading.Tasks;
            using Shared.Contracts.Chat;
            using Lakona.Game.Server.Hotfix;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Hotfix;

            [HotfixService(typeof(IChatService))]
            internal sealed class ChatService
            {
                public ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)
                {
                    return default;
                }
            }
            """;

        var result = GeneratorTestHost.RunWithGeneratedAppReference(
            appSource,
            hotfixSource,
            appAssemblyName: "Server.App",
            hotfixAssemblyName: "Server.Hotfix",
            appGlobalOptions: new Dictionary<string, string>
            {
                ["build_property.LakonaHotfixGenerateStableRpcServices"] = "true"
            },
            hotfixGlobalOptions: new Dictionary<string, string>
            {
                ["build_property.LakonaHotfixGenerateStableRpcServices"] = "false"
            });

        Assert.Empty(result.App.ErrorDiagnostics);
        Assert.Empty(result.Hotfix.ErrorDiagnostics);
        Assert.Contains("ChatServiceProxy", result.App.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceProxy", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatServiceEndpointBinder", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedHotfixRequiredServiceContracts", result.Hotfix.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Hotfix_service_call_exposes_current_session_constructor_contract()
    {
        Assert.True(typeof(Lakona.Game.Server.Hotfix.IHotfixServiceCall<object>)
            .IsAssignableFrom(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object>)));
        Assert.True(typeof(Lakona.Game.Server.Hotfix.IHotfixServiceCall<object>)
            .IsAssignableFrom(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object, object>)));

        var currentSessionProperty = typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<>)
            .GetProperty("CurrentSession");

        Assert.NotNull(currentSessionProperty);
        Assert.Equal(
            typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
            currentSessionProperty.PropertyType);

        var currentSessionItemsProperty = typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<>)
            .GetProperty("CurrentSessionItems");

        Assert.NotNull(currentSessionItemsProperty);
        Assert.Equal(
            typeof(Lakona.Game.Server.Sessions.GameSessionItems),
            currentSessionItemsProperty.PropertyType);
        Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object>).GetConstructor([
            typeof(object),
            typeof(string),
            typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
            typeof(IServiceProvider),
            typeof(Lakona.Game.Server.Actors.IActorRuntime),
            typeof(Lakona.Game.Server.ILakonaGameServer)
        ]));
        Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object>).GetConstructor([
            typeof(object),
            typeof(string),
            typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
            typeof(Lakona.Game.Server.Sessions.GameSessionItems),
            typeof(IServiceProvider),
            typeof(Lakona.Game.Server.Actors.IActorRuntime),
            typeof(Lakona.Game.Server.ILakonaGameServer)
        ]));
        Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object>).GetConstructor([
            typeof(object),
            typeof(string),
            typeof(IServiceProvider),
            typeof(Lakona.Game.Server.Actors.IActorRuntime),
            typeof(Lakona.Game.Server.ILakonaGameServer)
        ]));
        Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object, object>).GetConstructor([
            typeof(object),
            typeof(string),
            typeof(object),
            typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
            typeof(IServiceProvider),
            typeof(Lakona.Game.Server.Actors.IActorRuntime),
            typeof(Lakona.Game.Server.ILakonaGameServer)
        ]));
        Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object, object>).GetConstructor([
            typeof(object),
            typeof(string),
            typeof(object),
            typeof(Lakona.Game.Server.Sessions.GameSessionKey?),
            typeof(Lakona.Game.Server.Sessions.GameSessionItems),
            typeof(IServiceProvider),
            typeof(Lakona.Game.Server.Actors.IActorRuntime),
            typeof(Lakona.Game.Server.ILakonaGameServer)
        ]));
        Assert.NotNull(typeof(Lakona.Game.Server.Hotfix.HotfixServiceCall<object, object>).GetConstructor([
            typeof(object),
            typeof(string),
            typeof(object),
            typeof(IServiceProvider),
            typeof(Lakona.Game.Server.Actors.IActorRuntime),
            typeof(Lakona.Game.Server.ILakonaGameServer)
        ]));
    }

    [Fact]
    public async Task Hotfix_service_call_current_session_items_are_immutable_for_one_call()
    {
        var sessions = new InMemoryGameSessionRegistry();
        var session = await sessions.StartNewSessionAsync("player-a", TestContext.Current.CancellationToken);
        await sessions.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-a"), TestContext.Current.CancellationToken);
        var snapshot = await sessions.GetSessionItemsAsync(session, TestContext.Current.CancellationToken);
        var services = new ServiceCollection().AddLakonaGameServerActors().BuildServiceProvider();
        var call = new HotfixServiceCall<object>(
            new object(),
            "connection-a",
            session,
            snapshot,
            services,
            services.GetRequiredService<IActorRuntime>(),
            new RegistryBackedGameServer(sessions));

        await call.GameServer.SetSessionItemAsync(session, "roomId", GameSessionItemValue.FromString("room-b"), TestContext.Current.CancellationToken);

        Assert.Equal("room-a", call.CurrentSessionItems.GetString("roomId"));
        Assert.Equal("room-b", (await call.GameServer.GetSessionItemAsync(session, "roomId", TestContext.Current.CancellationToken))?.GetString());
    }

    [Fact]
    public void Generator_emits_required_contracts_without_manual_builder_extension()
    {
        var result = GeneratorTestHost.Run("""
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;
            namespace Demo;

            [RpcService(ApiName = "login")]
            public interface ILoginService
            {
                [RpcMethod(1)]
                ValueTask<LoginReply> LoginAsync(LoginRequest request);
            }

            public sealed class LoginRequest
            {
            }

            public sealed class LoginReply
            {
            }
            """);

        var generated = result.GeneratedSource;

        Assert.Contains("GeneratedHotfixRequiredServiceContracts", generated);
        Assert.Contains("IHotfixRequiredServiceContracts", generated);
        Assert.Contains("GetCurrentSessionAsync(_connectionId, global::System.Threading.CancellationToken.None)", generated);
        Assert.Contains("var currentSessionItems = currentSession is { } sessionKey", generated);
        Assert.Contains("GetSessionItemsAsync(sessionKey, global::System.Threading.CancellationToken.None)", generated);
        Assert.Contains("currentSession,", generated);
        Assert.Contains("currentSessionItems,", generated);
        Assert.Contains("public readonly struct LoginServiceCall<TRequest>", generated);
        Assert.Contains("IHotfixServiceCall<TRequest>", generated);
        Assert.DoesNotContain(" Callback => ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("UseGeneratedHotfixServices", generated);
    }

    [Fact]
    public void Generator_uses_rpc_method_id_for_result_returning_hotfix_call()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            namespace Shared.Contracts.Login
            {
                public sealed class LoginRequest
                {
                }

                public sealed class LoginReply
                {
                }

                public interface ILoginCallback
                {
                }

                [RpcService(10, NotificationContract = typeof(ILoginCallback))]
                public interface ILoginService
                {
                    [RpcMethod(9)]
                    ValueTask<LoginReply> LoginAsync(LoginRequest request);
                }
            }

            namespace Server.App.Generated
            {
                using System;
                using Lakona.Rpc.Server;
                using Shared.Contracts.Login;

                public sealed class LoginCallbackProxy : ILoginCallback
                {
                    public LoginCallbackProxy(RpcSession session)
                    {
                    }
                }

                public static class LoginServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, ILoginService> implFactory)
                    {
                    }
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("InvokeAsync<global::Shared.Contracts.Login.ILoginService", result.GeneratedSource);
        Assert.Contains("9,", result.GeneratedSource);
        Assert.Contains("global::Shared.Contracts.Login.LoginReply", result.GeneratedSource);
        Assert.DoesNotContain("nameof(LoginAsync)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LoginAsync\"", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_discovers_shared_rpc_service_contract_from_metadata_reference()
    {
        var sharedSource = """
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            namespace Shared.Contracts.Login
            {
                public sealed class LoginRequest
                {
                }

                public interface ILoginCallback
                {
                }

                [RpcService(10, NotificationContract = typeof(ILoginCallback))]
                public interface ILoginService
                {
                    [RpcMethod(9)]
                    ValueTask LoginAsync(LoginRequest request);
                }
            }
            """;

        var appSource = """
            namespace Server.App.Generated
            {
                using System;
                using Lakona.Rpc.Server;
                using Shared.Contracts.Login;

                public sealed class LoginCallbackProxy : ILoginCallback
                {
                    public LoginCallbackProxy(RpcSession session)
                    {
                    }
                }

                public static class LoginServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, ILoginService> implFactory)
                    {
                    }
                }
            }
            """;

        var result = GeneratorTestHost.RunWithReference(appSource, sharedSource);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("ILoginService", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("LoginServiceProxy", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[RpcService(1)] public interface IMissingRpcMethod { ValueTask PingAsync(Request request); }", "LKNHOTFIX007")]
    [InlineData("[RpcService(1)] public interface ITwoParameters { [RpcMethod(1)] ValueTask PingAsync(Request request, Request other); }", "LKNHOTFIX008")]
    [InlineData("[RpcService(1)] public interface IUnsupportedReturn { [RpcMethod(1)] Task PingAsync(Request request); }", "LKNHOTFIX009")]
    [InlineData("[RpcService(1, NotificationContract = typeof(BadCallback))] public interface IBadCallbackService { [RpcMethod(1)] ValueTask PingAsync(Request request); } public sealed class BadCallback { }", "LKNHOTFIX010")]
    public void Generator_reports_diagnostics_for_unsupported_hotfix_rpc_service_shapes(
        string contractSource,
        string diagnosticId)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Lakona.Rpc.Core;

            namespace Shared.Contracts
            {
                public sealed class Request
                {
                }

                {{contractSource}}
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.DoesNotContain("UnsupportedServiceProxy", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_emits_accessor_for_private_field()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                private int exp;
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("partial class PlayerState", result.GeneratedSource);
        Assert.Contains("public int __hotfix_exp()", result.GeneratedSource);
        Assert.Contains("return exp;", result.GeneratedSource);
    }

    [Fact]
    public void Generator_emits_accessor_for_underscore_private_field()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                private int _exp;
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public int __hotfix_exp()", result.GeneratedSource);
        Assert.Contains("return _exp;", result.GeneratedSource);
    }

    [Fact]
    public void Generator_reports_diagnostic_for_non_partial_state()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public class PlayerState
            {
                private int exp;
            }
            """;

        var result = GeneratorTestHost.Run(source);

        var diagnostic = Assert.Single(result.ErrorDiagnostics, static diagnostic => diagnostic.Id == "LKNHOTFIX001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Generated_accessor_output_compiles()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                private int exp;
            }

            public static class Reader
            {
                public static int Read(PlayerState state)
                {
                    return state.__hotfix_exp();
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
    }

    [Fact]
    public void Generator_emits_dispatch_wrapper_declaration_marker()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                private int exp;
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch", result.GeneratedSource);
    }

    [Fact]
    public void Partial_struct_state_emits_accessor_and_compiles()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial struct PlayerState
            {
                private int exp;
            }

            public static class Reader
            {
                public static int Read(PlayerState state)
                {
                    return state.__hotfix_exp();
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("partial struct PlayerState", result.GeneratedSource);
        Assert.Contains("public int __hotfix_exp()", result.GeneratedSource);
    }

    [Fact]
    public void Generic_state_emits_valid_source_and_compiles()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            public sealed class Payload
            {
            }

            [HotfixState]
            public partial class PlayerState<TPayload>
                where TPayload : class, new()
            {
                private TPayload payload = new TPayload();
            }

            public static class Reader
            {
                public static Payload Read(PlayerState<Payload> state)
                {
                    return state.__hotfix_payload();
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("partial class PlayerState<TPayload>", result.GeneratedSource);
        Assert.Contains("where TPayload : class, new()", result.GeneratedSource);
    }

    [Fact]
    public void Nested_partial_state_emits_inside_containing_type_and_compiles()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            public partial class Actor<T>
            {
                [HotfixState]
                public partial class State
                {
                    private T value = default!;
                }
            }

            public static class Reader
            {
                public static string Read(Actor<string>.State state)
                {
                    return state.__hotfix_value();
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public partial class Actor<T>", result.GeneratedSource);
        Assert.Contains("partial class State", result.GeneratedSource);
        Assert.Contains("public T __hotfix_value()", result.GeneratedSource);
    }

    [Fact]
    public void Nested_state_with_non_partial_containing_type_reports_diagnostic()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            public class Actor
            {
                [HotfixState]
                public partial class State
                {
                    private int exp;
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        var diagnostic = Assert.Single(result.ErrorDiagnostics, static diagnostic => diagnostic.Id == "LKNHOTFIX002");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Auto_property_backing_fields_are_ignored_and_output_compiles()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                public int Level { get; private set; }
                private int exp;
            }

            public static class Reader
            {
                public static int Read(PlayerState state)
                {
                    return state.__hotfix_exp();
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("__hotfix_exp", result.GeneratedSource);
        Assert.DoesNotContain("Level", result.GeneratedSource);
        Assert.DoesNotContain("k__BackingField", result.GeneratedSource);
    }

    [Fact]
    public void Static_and_const_private_fields_are_ignored()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                private static int sharedExp;
                private const int MaxExp = 10;
                private readonly int exp;
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("__hotfix_exp", result.GeneratedSource);
        Assert.DoesNotContain("__hotfix_sharedExp", result.GeneratedSource);
        Assert.DoesNotContain("__hotfix_MaxExp", result.GeneratedSource);
    }

    [Fact]
    public void Underscore_and_plain_private_fields_produce_unique_accessors_and_compile()
    {
        var source = """
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Demo;

            [HotfixState]
            public partial class PlayerState
            {
                private int _exp;
                private int exp;
            }

            public static class Reader
            {
                public static int Read(PlayerState state)
                {
                    return state.__hotfix_exp() + state.__hotfix__exp();
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public int __hotfix_exp()", result.GeneratedSource);
        Assert.Contains("public int __hotfix__exp()", result.GeneratedSource);
    }

    private sealed class RegistryBackedGameServer : ILakonaGameServer
    {
        private readonly IGameSessionRegistry _sessions;

        public RegistryBackedGameServer(IGameSessionRegistry sessions)
        {
            _sessions = sessions;
        }

        public ValueTask<GameSessionKey> StartSessionAsync(
            string ownerKey,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
            string ownerKey,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            throw new NotSupportedException();
        }

        public ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
            GameSessionResumeRequest request,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            throw new NotSupportedException();
        }

        public ValueTask BindSessionAsync<TCallback>(
            GameSessionKey session,
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            throw new NotSupportedException();
        }

        public ValueTask BindCurrentSessionAsync<TCallback>(
            string connectionId,
            TCallback callback,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            throw new NotSupportedException();
        }

        public ValueTask MarkSessionDisconnectedAsync(
            GameSessionKey session,
            string? connectionId = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            throw new NotSupportedException();
        }

        public ValueTask SetSessionItemAsync(
            GameSessionKey session,
            string key,
            GameSessionItemValue value,
            CancellationToken cancellationToken = default)
        {
            return _sessions.SetSessionItemAsync(session, key, value, cancellationToken);
        }

        public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
            GameSessionKey session,
            string key,
            CancellationToken cancellationToken = default)
        {
            return _sessions.GetSessionItemAsync(session, key, cancellationToken);
        }

        public ValueTask<GameSessionItems> GetSessionItemsAsync(
            GameSessionKey session,
            CancellationToken cancellationToken = default)
        {
            return _sessions.GetSessionItemsAsync(session, cancellationToken);
        }

        public ValueTask RemoveSessionItemAsync(
            GameSessionKey session,
            string key,
            CancellationToken cancellationToken = default)
        {
            return _sessions.RemoveSessionItemAsync(session, key, cancellationToken);
        }

        public ValueTask TerminateSessionAsync(
            GameSessionKey session,
            SessionTerminationReason reason,
            string? message = null,
            SessionTerminationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
