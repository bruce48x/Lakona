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
    }
}
