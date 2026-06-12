using Microsoft.CodeAnalysis;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Generators.Tests;

public sealed class HotfixGeneratorTests
{
    [Fact]
    public void Generator_emits_hotfix_rpc_service_proxy_and_binding_extension()
    {
        var source = """
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
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

            namespace Server.App.Services
            {
                using Shared.Contracts.Chat;

                [HotfixRpcService(typeof(IChatService), EndpointName = "control")]
                internal static partial class ChatServiceEndpoint;
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
        Assert.Contains("internal sealed class ChatServiceEndpointProxy : global::Shared.Contracts.Chat.IChatService", result.GeneratedSource);
        Assert.Contains("HotfixServiceCall<global::Shared.Contracts.Chat.ChatBindRequest, global::Shared.Contracts.Chat.IChatCallback>", result.GeneratedSource);
        Assert.Contains("            7,", result.GeneratedSource);
        Assert.Contains("global::Server.App.Generated.ChatServiceBinder.BindFactory", result.GeneratedSource);
        Assert.Contains("UseGeneratedHotfixServices", result.GeneratedSource);
    }

    [Fact]
    public void Generator_emits_named_binding_set_dispatch()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Lakona.Game.Server.Hotfix.Abstractions;
            using Lakona.Rpc.Core;
            using Lakona.Rpc.Server;

            namespace Shared.Contracts
            {
                public sealed class Request
                {
                }

                public interface IControlCallback
                {
                }

                public interface IRealtimeCallback
                {
                }

                [RpcService(1, NotificationContract = typeof(IControlCallback))]
                public interface IControlService
                {
                    [RpcMethod(1)]
                    ValueTask PingAsync(Request request);
                }

                [RpcService(2, NotificationContract = typeof(IRealtimeCallback))]
                public interface IRealtimeService
                {
                    [RpcMethod(1)]
                    ValueTask TickAsync(Request request);
                }
            }

            namespace Server.App.Services
            {
                using Shared.Contracts;

                [HotfixRpcService(typeof(IControlService), EndpointName = "control")]
                internal static partial class ControlServiceEndpoint;

                [HotfixRpcService(typeof(IRealtimeService), BindingSetName = "realtime", EndpointName = "realtime")]
                internal static partial class RealtimeServiceEndpoint;
            }

            namespace Server.App.Generated
            {
                using Shared.Contracts;

                public sealed class ControlCallbackProxy : IControlCallback
                {
                    public ControlCallbackProxy(RpcSession session)
                    {
                    }
                }

                public sealed class RealtimeCallbackProxy : IRealtimeCallback
                {
                    public RealtimeCallbackProxy(RpcSession session)
                    {
                    }
                }

                public static class ControlServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, IControlService> implFactory)
                    {
                    }
                }

                public static class RealtimeServiceBinder
                {
                    public static void BindFactory(RpcServiceRegistry registry, Func<RpcSession, IRealtimeService> implFactory)
                    {
                    }
                }
            }
            """;

        var result = GeneratorTestHost.Run(source);

        Assert.Empty(result.ErrorDiagnostics);
        Assert.Contains("BindGeneratedHotfixServices(registry, services, \"default\")", result.GeneratedSource);
        Assert.Contains("case \"default\":", result.GeneratedSource);
        Assert.Contains("global::Server.App.Generated.ControlServiceBinder.BindFactory", result.GeneratedSource);
        Assert.Contains("case \"realtime\":", result.GeneratedSource);
        Assert.Contains("global::Server.App.Generated.RealtimeServiceBinder.BindFactory", result.GeneratedSource);
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
