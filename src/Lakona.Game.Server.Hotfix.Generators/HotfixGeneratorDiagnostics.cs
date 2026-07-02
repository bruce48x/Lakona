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

        public static readonly DiagnosticDescriptor HotfixActorContractActorMustDeriveActor = new DiagnosticDescriptor(
            "ULGHOTFIX012",
            "Hotfix actor contract actor type must derive Actor<TKey>",
            "Hotfix actor contract '{0}' references actor type '{1}' that must derive Actor<TKey>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedHotfixActorContractReturnType = new DiagnosticDescriptor(
            "ULGHOTFIX013",
            "Unsupported hotfix actor contract method return type",
            "Hotfix actor contract method '{0}' must return ValueTask or ValueTask<T>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedHotfixActorContractParameters = new DiagnosticDescriptor(
            "ULGHOTFIX014",
            "Unsupported hotfix actor contract method parameters",
            "Hotfix actor contract method '{0}' must declare exactly one request parameter plus optional CancellationToken",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixActorContractMethod = new DiagnosticDescriptor(
            "ULGHOTFIX015",
            "Duplicate hotfix actor contract method signature",
            "Hotfix actor contract '{0}' has duplicate generated ref method signature '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedHotfixActorContractGenericShape = new DiagnosticDescriptor(
            "ULGHOTFIX016",
            "Unsupported generic hotfix actor contract shape",
            "Hotfix actor contract '{0}' uses unsupported generic actor contract shape '{1}'",
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

        public static readonly DiagnosticDescriptor HotfixFeatureMustInheritHotfixGameFeature = new DiagnosticDescriptor(
            "ULGHOTFIX022",
            "HotfixFeatureMustInheritHotfixGameFeature",
            "Hotfix feature '{0}' must inherit Lakona.Game.Server.Hotfix.Abstractions.HotfixGameFeature",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixFeatureConfigureShape = new DiagnosticDescriptor(
            "ULGHOTFIX023",
            "HotfixFeatureConfigureShape",
            "Hotfix feature '{0}' must declare exactly one public static void Configure(HotfixFeatureContext context) and no other public Configure overloads",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixFeatureLifecycleHookShape = new DiagnosticDescriptor(
            "ULGHOTFIX024",
            "HotfixFeatureLifecycleHookShape",
            "Hotfix feature '{0}' lifecycle hook '{1}' must declare public static ValueTask {1}({2} call)",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixFeatureOnReloadUnsupported = new DiagnosticDescriptor(
            "ULGHOTFIX025",
            "HotfixFeatureOnReloadUnsupported",
            "Hotfix feature '{0}' declares public OnReload, which is not supported",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
