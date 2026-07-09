using Microsoft.CodeAnalysis;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class HotfixGeneratorDiagnostics
    {
        public static readonly DiagnosticDescriptor StateMustBePartial = new DiagnosticDescriptor(
            "ULGHOTFIX001",
            "Hotfix state must be partial",
            "Hotfix state type '{0}' must be partial so friend accessors can be generated",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new DiagnosticDescriptor(
            "ULGHOTFIX002",
            "Hotfix state containing type must be partial",
            "Containing type '{0}' for hotfix state '{1}' must be partial so friend accessors can be generated",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedServiceContract = new DiagnosticDescriptor(
            "ULGHOTFIX006",
            "Unsupported hotfix RPC service contract",
            "Hotfix RPC service contract '{0}' must be an interface marked with [RpcService]",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RpcMethodAttributeRequired = new DiagnosticDescriptor(
            "ULGHOTFIX007",
            "Hotfix RPC service method must have RpcMethod",
            "RPC service method '{0}' must be marked with [RpcMethod]",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RpcMethodRequiresSingleRequest = new DiagnosticDescriptor(
            "ULGHOTFIX008",
            "Hotfix RPC service method must have one request parameter",
            "RPC service method '{0}' must declare exactly one request DTO parameter",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedRpcMethodReturnType = new DiagnosticDescriptor(
            "ULGHOTFIX009",
            "Unsupported hotfix RPC service method return type",
            "RPC service method '{0}' must return ValueTask or ValueTask<T>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedNotificationContract = new DiagnosticDescriptor(
            "ULGHOTFIX010",
            "Unsupported hotfix RPC notification contract",
            "Hotfix RPC service contract '{0}' has a notification contract that cannot be mapped to a generated callback proxy",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorMustNotDeclareBusinessMethod = new DiagnosticDescriptor(
            "ULGHOTFIX011",
            "Stable actor must not declare business methods",
            "Actor '{0}' declares method '{1}' in the stable app; move behavior to a [HotfixBehaviorOf] class in Server.Hotfix",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorTargetMustDeriveActor = new DiagnosticDescriptor(
            "ULGHOTFIX017",
            "Hotfix behavior target must derive Actor<TKey>",
            "Hotfix behavior '{0}' targets type '{1}', which must derive Lakona.Game.Server.Actors.Actor<TKey>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixBehaviorForActor = new DiagnosticDescriptor(
            "ULGHOTFIX018",
            "Actor must have exactly one hotfix behavior",
            "Actor '{0}' has multiple hotfix behavior classes; keep one '<ActorPrefix>Behavior' class and move subdomain code into helpers or partial files",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: null,
            helpLinkUri: null,
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public static readonly DiagnosticDescriptor HotfixBehaviorMustBeStaticPartial = new DiagnosticDescriptor(
            "ULGHOTFIX019",
            "Hotfix behavior must be static partial",
            "Hotfix behavior '{0}' must be a static partial class so generated behavior-owned actor wrappers can be emitted into the same type",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorNameMustMatchActor = new DiagnosticDescriptor(
            "ULGHOTFIX020",
            "Hotfix behavior name must match actor name",
            "Hotfix behavior '{0}' targets actor '{1}' and must be named '{2}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorActorApiTypeBoundary = new DiagnosticDescriptor(
            "ULGHOTFIX027",
            "Hotfix behavior actor API DTO must be stable",
            "Hotfix behavior actor API method '{0}' uses {1} type '{2}' from the hotfix assembly; request and result DTOs must live outside hotfix code",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorActorApiMethodShape = new DiagnosticDescriptor(
            "ULGHOTFIX028",
            "Unsupported hotfix behavior actor API method shape",
            "Hotfix behavior actor API method '{0}' must be a public static extension method whose receiver is '{1}', followed by exactly one request DTO and optional CancellationToken, and must return ValueTask or ValueTask<T>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixBehaviorActorApiMethodKey = new DiagnosticDescriptor(
            "ULGHOTFIX029",
            "Duplicate hotfix behavior actor API method key",
            "Hotfix behavior actor API method '{0}' has duplicate canonical actor API method key '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixBehaviorActorApiGeneratedSignature = new DiagnosticDescriptor(
            "ULGHOTFIX030",
            "Duplicate hotfix behavior actor API generated signature",
            "Hotfix behavior actor API method '{0}' has duplicate generated actor API signature '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
