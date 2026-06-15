using Microsoft.CodeAnalysis;

namespace Lakona.Game.Server.Generators
{
    internal static class TypedActorGeneratorDiagnostics
    {
        public static readonly DiagnosticDescriptor UnsupportedMethodSignature = new DiagnosticDescriptor(
            "LAKONA001",
            "Typed actor method signature is not supported",
            "Typed actor method '{0}' has an unsupported signature",
            "Lakona.Game.Actor",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
