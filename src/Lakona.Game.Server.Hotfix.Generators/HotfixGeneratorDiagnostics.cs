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
    }
}
