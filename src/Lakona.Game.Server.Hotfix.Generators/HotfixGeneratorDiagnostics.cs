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

        public static readonly DiagnosticDescriptor ServiceMarkerMustBePartial = new DiagnosticDescriptor(
            "ULGHOTFIX003",
            "Hotfix RPC service marker must be partial",
            "Hotfix RPC service marker '{0}' must be partial so generated service bindings can be attached",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateServiceMarker = new DiagnosticDescriptor(
            "ULGHOTFIX004",
            "Duplicate hotfix RPC service marker",
            "Hotfix RPC service marker '{0}' duplicates contract '{1}' in binding set '{2}'",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BindingSetEndpointMismatch = new DiagnosticDescriptor(
            "ULGHOTFIX005",
            "Hotfix RPC binding set has multiple endpoints",
            "Hotfix RPC binding set '{0}' declares multiple endpoint names; give each endpoint an explicit binding set name",
            "Lakona.Game.Hotfix",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
