using Microsoft.CodeAnalysis;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class HotfixGeneratorDiagnostics
    {
        public static readonly DiagnosticDescriptor LifecycleContract = new DiagnosticDescriptor(
            "LKNHOTFIX059", "Invalid Hotfix lifecycle contract",
            "Hotfix lifecycle '{0}': {1}", "Lakona.Game.Hotfix", DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor ComponentDependencyCycle = new DiagnosticDescriptor(
            "LKNHOTFIX057", "Hotfix component constructor dependency cycle",
            "Hotfix component constructor dependency cycle: {0}; remove the circular dependency or extract a shared dependency",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor PossibleComponentDependencyCycle = new DiagnosticDescriptor(
            "LKNHOTFIX058", "Possible Hotfix component constructor dependency cycle",
            "Default Hotfix component constructors form a cycle: {0}; custom service registration may change this graph; remove the cycle or verify the overriding factory/registration",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Warning, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor TimerArgsMustBeStable = new DiagnosticDescriptor(
            "LKNHOTFIX055", "Actor timer arguments must be stable",
            "Actor timer '{0}' uses Hotfix-defined args type '{1}'; move timer DTOs to Server.App or another stable assembly, including types nested in arrays or generics",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor TimerArgsShape = new DiagnosticDescriptor(
            "LKNHOTFIX056", "Unsupported Actor timer arguments",
            "Actor timer '{0}' has unsupported args at '{1}': {2}",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor StartupTypeShape = new DiagnosticDescriptor(
            "LKNHOTFIX050", "Invalid Hotfix startup root",
            "Hotfix startup '{0}' must be a public static non-generic class with public non-generic containing types; move configuration into a publicly visible non-generic root",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor DuplicateStartupRoot = new DiagnosticDescriptor(
            "LKNHOTFIX051", "Multiple Hotfix startup roots",
            "Hotfix assembly declares multiple startup roots: {0}; keep one [HotfixStartup] root and call configuration helpers explicitly",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.CompilationEnd);
        public static readonly DiagnosticDescriptor StartupRootRequired = new DiagnosticDescriptor(
            "LKNHOTFIX052", "Hotfix configuration requires a startup root",
            "Hotfix configuration method '{0}' must be declared in the assembly's [HotfixStartup] class; move it to that root or mark its containing class when no root exists",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor StartupMethodShape = new DiagnosticDescriptor(
            "LKNHOTFIX053", "Invalid Hotfix configuration method",
            "Hotfix configuration method '{0}' marked [{1}] must be public static non-generic synchronous void with one by-value {2} parameter and only one configuration attribute; use a synchronous configuration method",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor DuplicateStartupMethod = new DiagnosticDescriptor(
            "LKNHOTFIX054", "Multiple Hotfix configuration methods",
            "Hotfix startup '{0}' declares multiple [{1}] methods; keep one attributed method and call helpers explicitly",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorTimerMethodShape = new DiagnosticDescriptor(
            "LKNHOTFIX049", "Invalid Actor timer callback",
            "Actor timer '{0}' must belong to a Hotfix Actor Behavior and be an instance non-generic ValueTask method with (Actor, TimerTick<TArgs>) and only [ActorTimer]",
            "Lakona.Game.Hotfix", DiagnosticSeverity.Error, isEnabledByDefault: true);
        public static readonly DiagnosticDescriptor UnsupportedServiceContract = new DiagnosticDescriptor(
            "LKNHOTFIX006",
            "Unsupported hotfix RPC service contract",
            "Hotfix RPC service contract '{0}' must be an interface marked with [RpcService]",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RpcMethodAttributeRequired = new DiagnosticDescriptor(
            "LKNHOTFIX007",
            "Hotfix RPC service method must have RpcMethod",
            "RPC service method '{0}' must be marked with [RpcMethod]",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RpcMethodRequiresSingleRequest = new DiagnosticDescriptor(
            "LKNHOTFIX008",
            "Hotfix RPC service method must have one request parameter",
            "RPC service method '{0}' must declare exactly one request DTO parameter",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedRpcMethodReturnType = new DiagnosticDescriptor(
            "LKNHOTFIX009",
            "Unsupported hotfix RPC service method return type",
            "RPC service method '{0}' must return ValueTask or ValueTask<T>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedNotificationContract = new DiagnosticDescriptor(
            "LKNHOTFIX010",
            "Unsupported hotfix RPC notification contract",
            "Hotfix RPC service contract '{0}' has a notification contract that cannot be mapped to a generated callback proxy",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorMustNotDeclareBusinessMethod = new DiagnosticDescriptor(
            "LKNHOTFIX011",
            "Stable actor must not declare business methods",
            "Actor '{0}' declares method '{1}' in the stable app; move behavior to a [HotfixBehaviorOf] class in Server.Hotfix",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorTargetMustDeriveActor = new DiagnosticDescriptor(
            "LKNHOTFIX017",
            "Hotfix behavior target must derive Actor<TKey>",
            "Hotfix behavior '{0}' targets type '{1}', which must derive Lakona.Game.Server.Actors.Actor<TKey>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixBehaviorForActor = new DiagnosticDescriptor(
            "LKNHOTFIX018",
            "Actor must have exactly one hotfix behavior",
            "Actor '{0}' has multiple hotfix behavior classes; keep one '<ActorPrefix>Behavior' class and move subdomain code into helpers or partial files",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: null,
            helpLinkUri: null,
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public static readonly DiagnosticDescriptor HotfixBehaviorMustBeSealedPartial = new DiagnosticDescriptor(
            "LKNHOTFIX019",
            "Hotfix behavior must be sealed partial",
            "Hotfix behavior '{0}' must be a sealed partial class so one dependency-only instance can be activated per hotfix generation",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorNameMustMatchActor = new DiagnosticDescriptor(
            "LKNHOTFIX020",
            "Hotfix behavior name must match actor name",
            "Hotfix behavior '{0}' targets actor '{1}' and must be named '{2}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorActorApiTypeBoundary = new DiagnosticDescriptor(
            "LKNHOTFIX027",
            "Hotfix behavior actor API DTO must be stable",
            "Hotfix behavior actor API method '{0}' uses {1} type '{2}' from the hotfix assembly; request and result DTOs must live outside hotfix code",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixBehaviorActorApiMethodShape = new DiagnosticDescriptor(
            "LKNHOTFIX028",
            "Unsupported hotfix behavior actor API method shape",
            "Hotfix behavior actor entry method '{0}' must be a public instance method whose first parameter is '{1}', followed by exactly one request DTO (no CancellationToken), and must return ValueTask or ValueTask<T>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixBehaviorActorApiMethodKey = new DiagnosticDescriptor(
            "LKNHOTFIX029",
            "Duplicate hotfix behavior actor API method key",
            "Hotfix behavior actor API method '{0}' has duplicate canonical actor API method key '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHotfixBehaviorActorApiGeneratedSignature = new DiagnosticDescriptor(
            "LKNHOTFIX030",
            "Duplicate hotfix behavior actor API generated signature",
            "Hotfix behavior actor API method '{0}' has duplicate generated actor API signature '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorStateMemberAccessMustStayInBehavior = new DiagnosticDescriptor(
            "LKNHOTFIX031",
            "Non-public actor state is behavior-owned",
            "Actor state member '{0}.{1}' is non-public and may only be accessed from the actor itself or its [HotfixBehaviorOf] class",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixModuleMustNotOwnData = new DiagnosticDescriptor(
            "LKNHOTFIX032",
            "Hotfix module may only capture constructor dependencies",
            "Hotfix module '{0}' member '{1}' stores generation data; only private readonly members assigned directly from an activation constructor parameter are allowed",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixServiceModuleShape = new DiagnosticDescriptor(
            "LKNHOTFIX035",
            "Hotfix service module must be generation-scoped",
            "Hotfix service or lifecycle module '{0}' must be a sealed non-generic concrete class with one selectable public activation constructor",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixServiceEntryMustBeInstance = new DiagnosticDescriptor(
            "LKNHOTFIX036",
            "Hotfix service entry must be an instance method",
            "Hotfix service or lifecycle entry method '{0}' must be an instance method",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixConcreteTypeRequiresRole = new DiagnosticDescriptor(
            "LKNHOTFIX037",
            "Class in a hotfix project must declare a hotfix role",
            "Class '{0}' belongs to a hotfix project but has no hotfix role; annotate it with [HotfixComponent] or move it to a stable assembly",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixStaticStateForbidden = new DiagnosticDescriptor(
            "LKNHOTFIX038",
            "Hotfix utility must not own static state",
            "Hotfix static type '{0}' member '{1}' stores static state; keep utility types pure and move data to a stable owner",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixComponentModuleShape = new DiagnosticDescriptor(
            "LKNHOTFIX039",
            "Hotfix component must be generation-scoped",
            "Hotfix component '{0}' must be a top-level sealed non-generic concrete class with one selectable public activation constructor",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HotfixMethodSelectorShape = new DiagnosticDescriptor(
            "LKNHOTFIX040",
            "Hotfix method selector must directly select an instance method",
            "Hotfix method selector must use the form 'static module => module.Method'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HttpManagementRouteReserved = new DiagnosticDescriptor(
            "LKNHOTFIX041",
            "Management HTTP route is reserved",
            "Application HTTP service method '{0}' cannot expose reserved Management route '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HttpServiceContractShape = new DiagnosticDescriptor(
            "LKNHOTFIX042",
            "Unsupported Application HTTP service",
            "Application HTTP service '{0}' must be a top-level public sealed non-generic class with a non-empty unique service name and at least one endpoint",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor HttpEndpointMethodShape = new DiagnosticDescriptor(
            "LKNHOTFIX043",
            "Unsupported Application HTTP endpoint method",
            "Application HTTP method '{0}' must have one [LakonaHttpEndpoint(method, route)], accept LakonaHttpCall, and return ValueTask<LakonaHttpResponse>",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHttpServiceName = new DiagnosticDescriptor(
            "LKNHOTFIX044",
            "Duplicate Application HTTP service name",
            "Application HTTP service name '{0}' is declared by more than one Hotfix class",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHttpEndpoint = new DiagnosticDescriptor(
            "LKNHOTFIX045",
            "Duplicate Application HTTP endpoint",
            "Application HTTP service '{0}' contains duplicate route '{1}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorMethodWireNameRequired = new DiagnosticDescriptor(
            "LKNHOTFIX046",
            "Actor method wire name is required",
            "Hotfix behavior actor API method '{0}' must declare a non-empty [ActorMethod] wire name",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ActorMethodCannotBeIgnored = new DiagnosticDescriptor(
            "LKNHOTFIX047",
            "Actor method cannot also be ignored",
            "Hotfix behavior method '{0}' cannot declare both [ActorMethod] and [ActorIgnore]",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidProjectRole = new DiagnosticDescriptor(
            "LKNHOTFIX048",
            "Invalid Lakona project role",
            "LakonaProjectRole '{0}' is invalid. Expected 'ServerApp' or 'Hotfix'.",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.CompilationEnd);

    }
}
