using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lakona.Game.Server.Hotfix.Generators
{
    internal static class GeneratorSymbolFacts
    {
        internal static bool IsException(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.SpecialType == SpecialType.System_Object) break;
                if (current.ToDisplayString() == "System.Exception") return true;
            }

            return false;
        }

        internal static bool HasFileModifier(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(static modifier =>
                string.Equals(modifier.ValueText, "file", System.StringComparison.Ordinal));
        }

        internal static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            return symbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == metadataName);
        }

        internal static string GetRuntimeTypeIdentity(ITypeSymbol type)
        {
            var assemblyName = GetRuntimeAssembly(type)?.Identity.Name ?? string.Empty;
            return GetRuntimeTypeFullName(type) + ", " + assemblyName;
        }

        internal static string GetRuntimeTypeFullName(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                var suffix = arrayType.IsSZArray ? "[]" :
                    arrayType.Rank == 1 ? "[*]" : "[" + new string(',', arrayType.Rank - 1) + "]";
                return GetRuntimeTypeFullName(arrayType.ElementType) + suffix;
            }

            if (type is INamedTypeSymbol namedType)
            {
                var containingTypes = new Stack<INamedTypeSymbol>();
                for (INamedTypeSymbol? current = namedType; current != null; current = current.ContainingType)
                {
                    containingTypes.Push(current);
                }

                var typeName = string.Join("+", containingTypes.Select(static type => type.MetadataName));
                // Reflection includes enclosing generic arguments, even for a non-generic leaf.
                var arguments = containingTypes.SelectMany(static type => type.TypeArguments).ToArray();
                if (arguments.Length > 0)
                {
                    typeName += "[[" +
                        string.Join("],[", arguments.Select(GetRuntimeAssemblyQualifiedTypeIdentity)) +
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
            var assemblyDisplayName = GetRuntimeAssembly(type)?.Identity.GetDisplayName();
            return string.IsNullOrEmpty(assemblyDisplayName)
                ? GetRuntimeTypeFullName(type)
                : GetRuntimeTypeFullName(type) + ", " + assemblyDisplayName;
        }

        private static IAssemblySymbol? GetRuntimeAssembly(ITypeSymbol type)
        {
            // Array symbols have no declaring assembly; Type.Assembly uses the element's assembly.
            while (type is IArrayTypeSymbol arrayType)
                type = arrayType.ElementType;
            return type.ContainingAssembly;
        }

        internal static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
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

        internal static bool IsPartial(TypeDeclarationSyntax declaration)
        {
            return declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        }

        internal static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
