using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixGeneratorTests
{
    private static readonly string ForbiddenGameEndpointType = string.Concat("Game", "Endpoint", "Name");

    [Fact]
    public void Generator_emits_behavior_first_actor_refs_from_hotfix_actor_contract()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);

            public sealed class LoginRequest
            {
                public string Password { get; set; } = "";
            }

            public sealed class LoginReply
            {
            }

            public sealed class UserActor : Actor<UserId>
            {
                internal int LoginCount;
            }

            [HotfixActorContract(typeof(UserActor))]
            public interface IUserActorContract
            {
                ValueTask<LoginReply> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("public sealed class UserActors", result.GeneratedSource);
        Assert.Contains("public UserRef Get(global::Game.Server.UserId id)", result.GeneratedSource);
        Assert.Contains("public UserLocalRef Local(global::Game.Server.UserId id)", result.GeneratedSource);
        Assert.Contains("public UserRemoteRef Remote(global::Lakona.Game.Cluster.NodeId nodeId, global::Game.Server.UserId id)", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.ActorId.From(_id.ToString())", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Hotfix.Dispatch.HotfixDispatch.InvokeValueTaskAsync<global::Game.Server.LoginReply>", result.GeneratedSource);
        Assert.Contains("\"Game.Server.IUserActorContract.login.Game.Server.LoginRequest\"", result.GeneratedSource);
        Assert.Contains("case \"Game.Server.IUserActorContract.login.Game.Server.LoginRequest\":", result.GeneratedSource);
        Assert.Contains("_directoryCache.TryGet(actorId, out var node)", result.GeneratedSource);
        Assert.Contains("_directory.ResolveAsync(actorId, cancellationToken)", result.GeneratedSource);
        Assert.Contains("_remote.AskAsync(invocation, cancellationToken)", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorCall.EnsureReplied", result.GeneratedSource);
        Assert.Contains("return _serializer.Deserialize<global::Game.Server.LoginReply>(result.Payload);", result.GeneratedSource);
        Assert.Contains("public sealed class UserActorClusterHandler", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Actors.RemoteActorGateway.SendReplyAsync(", result.GeneratedSource);
        Assert.Contains("_router,", result.GeneratedSource);
        Assert.Contains("envelope.SourceNode,", result.GeneratedSource);
        Assert.Contains("envelope.ReplyCorrelationId,", result.GeneratedSource);
        Assert.Contains("_serializer.Serialize(reply),", result.GeneratedSource);
        Assert.Contains("global::Lakona.Game.Server.Hosting.ILakonaGameGeneratedServiceRegistration", result.GeneratedSource);
        Assert.Contains("TryAddSingleton<global::Game.Server.UserActors>", result.GeneratedSource);
        Assert.DoesNotContain("actor.LoginAsync", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_namespaces_behavior_first_actor_remote_kinds_per_actor()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public readonly record struct RoomId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }
            public sealed class RoomActor : Actor<RoomId> { }

            [HotfixActorContract(typeof(UserActor))]
            public interface IUserActorContract
            {
                ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default);
            }

            [HotfixActorContract(typeof(RoomActor))]
            public interface IRoomActorContract
            {
                ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("\"Game.Server.IUserActorContract.ping.Game.Server.PingRequest\"", result.GeneratedSource);
        Assert.Contains("\"Game.Server.IRoomActorContract.ping.Game.Server.PingRequest\"", result.GeneratedSource);
        Assert.Contains("case \"Game.Server.IUserActorContract.ping.Game.Server.PingRequest\":", result.GeneratedSource);
        Assert.Contains("case \"Game.Server.IRoomActorContract.ping.Game.Server.PingRequest\":", result.GeneratedSource);
        Assert.DoesNotContain("case \"ping\":", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(", \"user\", \"ping\",", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(", \"room\", \"ping\",", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_namespaces_behavior_first_actor_remote_kinds_by_full_contract_identity()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.One
            {
                public readonly record struct UserId(string Value);
                public sealed class PingRequest { }
                public sealed class UserActor : Actor<UserId> { }

                [HotfixActorContract(typeof(UserActor))]
                public interface IUserActorContract
                {
                    ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default);
                }
            }

            namespace Game.Two
            {
                public readonly record struct UserId(string Value);
                public sealed class PingRequest { }
                public sealed class UserActor : Actor<UserId> { }

                [HotfixActorContract(typeof(UserActor))]
                public interface IUserActorContract
                {
                    ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default);
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("\"Game.One.IUserActorContract.ping.Game.One.PingRequest\"", result.GeneratedSource);
        Assert.Contains("\"Game.Two.IUserActorContract.ping.Game.Two.PingRequest\"", result.GeneratedSource);
        Assert.Contains("case \"Game.One.IUserActorContract.ping.Game.One.PingRequest\":", result.GeneratedSource);
        Assert.Contains("case \"Game.Two.IUserActorContract.ping.Game.Two.PingRequest\":", result.GeneratedSource);
        Assert.DoesNotContain("case \"user.ping\":", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_namespaces_behavior_first_actor_remote_kinds_by_request_type_for_overloads()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingUserRequest { }
            public sealed class PingAdminRequest { }
            public sealed class UserActor : Actor<UserId> { }

            [HotfixActorContract(typeof(UserActor))]
            public interface IUserActorContract
            {
                ValueTask PingAsync(PingUserRequest request, CancellationToken cancellationToken = default);
                ValueTask PingAsync(PingAdminRequest request, CancellationToken cancellationToken = default);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("\"Game.Server.IUserActorContract.ping.Game.Server.PingUserRequest\"", result.GeneratedSource);
        Assert.Contains("\"Game.Server.IUserActorContract.ping.Game.Server.PingAdminRequest\"", result.GeneratedSource);
        Assert.Contains("case \"Game.Server.IUserActorContract.ping.Game.Server.PingUserRequest\":", result.GeneratedSource);
        Assert.Contains("case \"Game.Server.IUserActorContract.ping.Game.Server.PingAdminRequest\":", result.GeneratedSource);
        Assert.DoesNotContain("case \"Game.Server.IUserActorContract.ping\":", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_diagnostic_when_hotfix_actor_contract_actor_does_not_derive_actor()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public sealed class NotActor { }
            public sealed class PingRequest { }

            [HotfixActorContract(typeof(NotActor))]
            public interface IUserActorContract
            {
                ValueTask PingAsync(PingRequest request);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "ULGHOTFIX012");
        Assert.Equal(9, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain("UserActors", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_diagnostic_for_unsupported_hotfix_actor_contract_method()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }

            [HotfixActorContract(typeof(UserActor))]
            public interface IUserActorContract
            {
                Task PingAsync(PingRequest request);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "ULGHOTFIX013");
        Assert.DoesNotContain("UserActors", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_diagnostic_for_invalid_hotfix_actor_contract_parameter_shape()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }

            [HotfixActorContract(typeof(UserActor))]
            public interface IUserActorContract
            {
                ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken, string extra);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "ULGHOTFIX014");
        Assert.Equal(14, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain("UserActors", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_reports_diagnostic_for_duplicate_hotfix_actor_contract_signature()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Actors;
            using Lakona.Game.Server.Hotfix.Abstractions;

            namespace Game.Server;

            public readonly record struct UserId(string Value);
            public sealed class PingRequest { }
            public sealed class UserActor : Actor<UserId> { }

            [HotfixActorContract(typeof(UserActor))]
            public interface IUserActorContract
            {
                ValueTask PingAsync(PingRequest request);
                ValueTask PingAsync(PingRequest request, CancellationToken cancellationToken = default);
            }
            """;

        var result = GeneratorTestHost.Run(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "ULGHOTFIX015");
        Assert.Equal(15, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain("UserActors", result.GeneratedSource, StringComparison.Ordinal);
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
        Assert.Contains("HotfixServiceCall<global::Shared.Contracts.Chat.ChatBindRequest, global::Shared.Contracts.Chat.IChatCallback>", result.GeneratedSource);
        Assert.Contains("var snapshot = _hotfixRuntime.Current;", result.GeneratedSource);
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
    [InlineData("[RpcService(1)] public interface IMissingRpcMethod { ValueTask PingAsync(Request request); }", "ULGHOTFIX007")]
    [InlineData("[RpcService(1)] public interface ITwoParameters { [RpcMethod(1)] ValueTask PingAsync(Request request, Request other); }", "ULGHOTFIX008")]
    [InlineData("[RpcService(1)] public interface IUnsupportedReturn { [RpcMethod(1)] Task PingAsync(Request request); }", "ULGHOTFIX009")]
    [InlineData("[RpcService(1, NotificationContract = typeof(BadCallback))] public interface IBadCallbackService { [RpcMethod(1)] ValueTask PingAsync(Request request); } public sealed class BadCallback { }", "ULGHOTFIX010")]
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

        var diagnostic = Assert.Single(result.ErrorDiagnostics, static diagnostic => diagnostic.Id == "ULGHOTFIX001");
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

        var diagnostic = Assert.Single(result.ErrorDiagnostics, static diagnostic => diagnostic.Id == "ULGHOTFIX002");
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
}
