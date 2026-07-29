using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lakona.Game.Server.Hotfix.Generators
{
    public sealed partial class HotfixGenerator
    {
        private static bool HasFileModifier(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(static modifier =>
                string.Equals(modifier.ValueText, "file", System.StringComparison.Ordinal));
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            return symbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == metadataName);
        }

        private static string GetRuntimeTypeIdentity(ITypeSymbol type)
        {
            var assemblyName = type.ContainingAssembly?.Identity.Name ?? string.Empty;
            return GetRuntimeTypeFullName(type) + ", " + assemblyName;
        }

        private static string GetRuntimeTypeFullName(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                return GetRuntimeTypeFullName(arrayType.ElementType) + "[]";
            }

            if (type is INamedTypeSymbol namedType)
            {
                var containingTypes = new Stack<string>();
                for (INamedTypeSymbol? current = namedType; current != null; current = current.ContainingType)
                {
                    containingTypes.Push(current.MetadataName);
                }

                var typeName = string.Join("+", containingTypes);
                if (namedType.IsGenericType && namedType.TypeArguments.Length > 0)
                {
                    typeName += "[[" +
                        string.Join("],[", namedType.TypeArguments.Select(GetRuntimeAssemblyQualifiedTypeIdentity)) +
                        "]]";
                }

                var namespaceName = namedType.ContainingNamespace.IsGlobalNamespace
                    ? string.Empty
                    : namedType.ContainingNamespace.ToDisplayString() + ".";
                return namespaceName + typeName;
            }

            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
        }

        private static string GetRuntimeAssemblyQualifiedTypeIdentity(ITypeSymbol type)
        {
            var assemblyDisplayName = type.ContainingAssembly?.Identity.GetDisplayName();
            return string.IsNullOrEmpty(assemblyDisplayName)
                ? GetRuntimeTypeFullName(type)
                : GetRuntimeTypeFullName(type) + ", " + assemblyDisplayName;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                foreach (var nested in EnumerateTypes(type))
                {
                    yield return nested;
                }
            }

            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in EnumerateTypes(childNamespace))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamedTypeSymbol type)
        {
            yield return type;
            foreach (var nested in type.GetTypeMembers())
            {
                foreach (var item in EnumerateTypes(nested))
                {
                    yield return item;
                }
            }
        }

        private static bool IsPartial(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
